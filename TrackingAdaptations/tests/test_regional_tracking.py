"""Cases motivated by real logs: P2 loss, global 3->1 filtering and hand drift."""
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
import json
from unittest.mock import patch
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from region_tracking import (RegionalPoseDetector, RegionalRoleTracker, validate_region_configuration,
                             map_landmarks, region_diagnostic, plausible_single_body, LandmarkHold)
from identity_tracking import TrackingSession
from interaction_support import upper_body_geometry


def pose(x=.5, shoulder_width=.2, shoulder_y=.4, left_y=.6, right_y=.6):
    points = [SimpleNamespace(x=x, y=.5, z=0., visibility=.9, presence=.9) for _ in range(33)]
    for index, xy in {0: (x, .2), 7: (x + shoulder_width * .25, .19),
                      8: (x - shoulder_width * .25, .19),
                      11: (x + shoulder_width / 2, shoulder_y),
                      12: (x - shoulder_width / 2, shoulder_y),
                      15: (x + .12, left_y), 16: (x - .12, right_y)}.items():
        points[index].x, points[index].y = xy
    return points


class RegionalTests(unittest.TestCase):
    def test_flexible_candidates_keep_roles_when_crossing_region_boundary(self):
        tracker = RegionalRoleTracker(clock=lambda: 0.)
        first, reset = tracker.assign_candidates([(.30, .4), (.50, .4), (.80, .4)])
        self.assertEqual(first, {1: 0, 2: 1, 3: 2})
        self.assertFalse(reset)
        # P1 has crossed the nominal one-third boundary.  Position matching,
        # rather than the current crop, keeps it assigned to P1.
        second, reset = tracker.assign_candidates([(.38, .4), (.50, .4), (.80, .4)])
        self.assertEqual(second, {1: 0, 2: 1, 3: 2})
        self.assertFalse(reset)

    def test_flexible_tracker_freezes_on_ambiguous_crossing(self):
        tracker = RegionalRoleTracker(clock=lambda: 0.)
        tracker.assign_candidates([(.25, .4), (.50, .4), (.75, .4)])
        assigned, reset = tracker.assign_candidates([(.49, .4), (.51, .4), (.75, .4)])
        self.assertEqual(assigned, {})
        self.assertFalse(reset)
        self.assertTrue(tracker.ambiguous)
        self.assertIn('paused', tracker.status)

    def test_flexible_tracker_requires_three_people_to_seed(self):
        tracker = RegionalRoleTracker(clock=lambda: 0.)
        assigned, reset = tracker.assign_candidates([(.25, .4), (.75, .4)])
        self.assertEqual(assigned, {})
        self.assertFalse(reset)
        self.assertIsNone(tracker.positions)

    def test_flexible_tracker_long_loss_invalidates_once(self):
        clock = [0.]
        tracker = RegionalRoleTracker(identity_timeout=1., clock=lambda: clock[0])
        tracker.assign_candidates([(.2, .4), (.5, .4), (.8, .4)])
        clock[0] = 1.1
        assigned, reset = tracker.assign_candidates([])
        self.assertEqual(assigned, {})
        self.assertTrue(reset)
        assigned, reset = tracker.assign_candidates([])
        self.assertEqual(assigned, {})
        self.assertFalse(reset)

    def test_three_independent_num_poses_one_tasks_and_roi_coordinate_mapping(self):
        options, tasks = [], []
        def factory(option):
            options.append(option)
            task = SimpleNamespace(detect_for_video=lambda *_: SimpleNamespace(pose_landmarks=[pose()]),
                                   close=lambda: None)
            tasks.append(task)
            return task
        vision = SimpleNamespace(PoseLandmarkerOptions=lambda **kwargs: SimpleNamespace(**kwargs),
                                 RunningMode=SimpleNamespace(VIDEO='VIDEO'))
        python = SimpleNamespace(BaseOptions=lambda **kwargs: SimpleNamespace(**kwargs))
        mp = SimpleNamespace(Image=lambda **kwargs: SimpleNamespace(**kwargs), ImageFormat=SimpleNamespace(SRGB='SRGB'))
        detector = RegionalPoseDetector('model', vision, python, mp, detector_factory=factory)
        try:
            people, displayed, diagnostics = detector.detect(np.zeros((720, 1280, 3), np.uint8), 1000)
            self.assertEqual([option.num_poses for option in options], [1, 1, 1])
            self.assertEqual(set(people), {1, 2, 3})
            self.assertEqual(len(displayed), 3)
            self.assertTrue(all(item['shoulderReady'] for item in diagnostics))
            self.assertAlmostEqual(people[2][0].x, .5, places=3)
        finally:
            detector.close()

    def test_missing_region_does_not_hide_other_roles_or_reassign_them(self):
        clock = [0.]
        tracker = RegionalRoleTracker(clock=lambda: clock[0])
        assigned, reset = tracker.assign({1: (.2, .4), 3: (.8, .4)})
        self.assertEqual(set(assigned), {1, 3})
        self.assertFalse(reset)
        clock[0] = .1
        assigned, reset = tracker.assign({1: (.2, .4), 2: (.5, .4), 3: (.8, .4)})
        self.assertEqual(set(assigned), {1, 2, 3})
        self.assertFalse(reset)

    def test_overlapping_rois_do_not_assign_same_body_three_times(self):
        boundaries = [0., 1/3, 2/3, 1.]
        created = []
        def factory(option):
            slot = len(created) + 1
            left = max(0, int((boundaries[slot-1]-.1)*1280))
            right = min(1280, int(np.ceil((boundaries[slot]+.1)*1280)))
            local_x = (.25*1280-left)/(right-left)
            engine = SimpleNamespace(detect_for_video=lambda *_: SimpleNamespace(pose_landmarks=[pose(local_x, .15)]),close=lambda:None)
            created.append(engine)
            return engine
        vision = SimpleNamespace(PoseLandmarkerOptions=lambda **kwargs: kwargs,RunningMode=SimpleNamespace(VIDEO='VIDEO'))
        python = SimpleNamespace(BaseOptions=lambda **kwargs: kwargs)
        mp = SimpleNamespace(Image=lambda **kwargs:SimpleNamespace(**kwargs),ImageFormat=SimpleNamespace(SRGB='SRGB'))
        detector = RegionalPoseDetector('model',vision,python,mp,detector_factory=factory)
        try:
            people,_,diagnostics=detector.detect(np.zeros((720,1280,3),np.uint8),1000)
            self.assertEqual(set(people),{1})
            self.assertIn('another region',diagnostics[1]['reason'])
            self.assertIn('another region',diagnostics[2]['reason'])
        finally:detector.close()

    def test_fixed_region_uses_its_own_body_when_a_neighbour_is_also_in_the_crop(self):
        # Middle crop spans .2333-.7667 of the frame; local .03 is a neighbour at ~.25,
        # local .5 is the P2 performer at .5. The neighbour is listed first on purpose.
        def factory(option):
            return SimpleNamespace(detect_for_video=lambda *_: SimpleNamespace(pose_landmarks=[pose(.03, .3), pose(.5, .3)]),
                                   close=lambda: None)
        vision = SimpleNamespace(PoseLandmarkerOptions=lambda **kwargs: SimpleNamespace(**kwargs),
                                 RunningMode=SimpleNamespace(VIDEO='VIDEO'))
        python = SimpleNamespace(BaseOptions=lambda **kwargs: SimpleNamespace(**kwargs))
        mp = SimpleNamespace(Image=lambda **kwargs: SimpleNamespace(**kwargs), ImageFormat=SimpleNamespace(SRGB='SRGB'))
        detector = RegionalPoseDetector('model', vision, python, mp, padding=.1, detector_factory=factory,
                                        num_poses_per_crop=2)
        try:
            landmarks, shown, diagnostic = detector.detect_region(2, np.zeros((720, 1280, 3), np.uint8), 1000)
            self.assertAlmostEqual(landmarks[0].x, .5, places=2)
            self.assertEqual(len(shown), 1)  # the neighbour is not drawn as an extra skeleton
            self.assertTrue(diagnostic['shoulderReady'])
        finally:
            detector.close()

    def test_pose_stitched_from_two_neighbours_is_rejected(self):
        self.assertTrue(plausible_single_body(pose(.5)))
        stitched = pose(.5)
        stitched[0].x = .5 + .2  # face belongs to the neighbour, one shoulder width away
        self.assertFalse(plausible_single_body(stitched))
        turned = pose(.5)
        turned[0].x = .5 + .08  # a head turn keeps the nose near the shoulder midpoint
        self.assertTrue(plausible_single_body(turned))

    def test_p2_short_loss_recovers_same_region_without_manual_latch(self):
        clock = [0.]
        tracker = RegionalRoleTracker(clock=lambda: clock[0])
        people = {1: (.2, .4), 2: (.5, .4), 3: (.8, .4)}
        tracker.assign(people)
        clock[0] = .5
        assigned, reset = tracker.assign({1: people[1], 3: people[3]})
        self.assertEqual(set(assigned), {1, 3})
        self.assertFalse(reset)
        clock[0] = 1.5
        self.assertFalse(tracker.assign(people)[1])

    def test_long_loss_invalidates_once_and_recovery_still_accepts_fixed_slot(self):
        clock = [0.]
        tracker = RegionalRoleTracker(clock=lambda: clock[0])
        tracker.assign({2: (.5, .4)})
        clock[0] = 2.1
        self.assertTrue(tracker.assign({})[1])
        clock[0] = 2.2
        self.assertFalse(tracker.assign({})[1])
        clock[0] = 3.
        assigned, reset = tracker.assign({2: (.5, .4)})
        self.assertEqual(set(assigned), {2})
        self.assertFalse(reset)

    def test_large_position_jump_invalidates_calibration(self):
        tracker = RegionalRoleTracker(clock=lambda: 0.)
        tracker.assign({1: (.2, .4)})
        self.assertTrue(tracker.assign({1: (.2, .7)})[1])

    def test_low_confidence_shoulder_is_diagnostic_not_promoted_to_input(self):
        points = pose()
        points[11].visibility = .49
        diagnostic = region_diagnostic(2, points, [0., 1/3, 2/3, 1.], 1)
        self.assertFalse(diagnostic['shoulderReady'])
        self.assertIn('steering', diagnostic['reason'])
        self.assertFalse(upper_body_geometry(points)['present'])

    def test_roi_landmarks_remap_hands_before_cup_association(self):
        points = map_landmarks(pose(.5), 640, 0, 640, 720, 1280, 720)
        self.assertAlmostEqual(points[0].x, .75)
        self.assertAlmostEqual(points[15].x, .81)
        self.assertAlmostEqual(points[15].y, .6)

    def test_extrapolated_crop_wrist_does_not_become_valid_after_global_mapping(self):
        local = pose()
        local[15].x = 1.1
        mapped = map_landmarks(local, 300, 0, 400, 720, 1280, 720)
        self.assertLess(mapped[15].x, 1.) # in full frame, but never observed in ROI
        self.assertEqual(mapped[15].visibility, 0.)
        geometry = upper_body_geometry(mapped)
        self.assertEqual(geometry['leftHandConfidence'], 0.)
        self.assertEqual((geometry['leftWristX'], geometry['leftWristY']), (0., 0.))

    def test_region_configuration_rejects_small_overlapping_and_nonfinite_regions(self):
        for boundaries, padding in [([0,.1,.6,1], .1), ([0,.7,.5,1], .1),
                                    ([0,float('nan'),.6,1],.1), ([0,.3,.6,1], .2),
                                    ([False,.3,.6,1],.1)]:
            with self.assertRaises(ValueError):
                validate_region_configuration(boundaries, padding)

    def test_repeated_region_request_does_not_repeatedly_recalibrate(self):
        session = TrackingSession(3, True)
        request = dict(version=1, performerCount=3, upperBodyOnly=True,
                       regionBoundaries=[0.,.3,.7,1.], regionPadding=.12)
        self.assertTrue(session.apply_command(request))
        self.assertFalse(session.apply_command(request))
        self.assertEqual(session.revision, 2)


class GeometryTests(unittest.TestCase):
    def test_joint_coordinates_are_same_global_frame_as_geometry(self):
        points = map_landmarks(pose(.5), 640, 0, 640, 720, 1280, 720)
        geometry = upper_body_geometry(points)
        self.assertTrue(geometry['present'])
        self.assertEqual(geometry['shoulderConfidence'], .9)
        for side, shoulder_index, wrist_index in [('left', 11, 15), ('right', 12, 16)]:
            self.assertEqual(geometry[side + 'ShoulderX'], points[shoulder_index].x)
            self.assertEqual(geometry[side + 'ShoulderY'], points[shoulder_index].y)
            self.assertEqual(geometry[side + 'WristX'], points[wrist_index].x)
            self.assertEqual(geometry[side + 'WristY'], points[wrist_index].y)
            self.assertAlmostEqual(geometry[side + 'HandVertical'],
                                   geometry[side + 'ShoulderY'] - geometry[side + 'WristY'])

    def test_low_confidence_or_missing_shoulder_cannot_publish_joint_geometry(self):
        for index, attribute, value in [(11, 'visibility', .49), (12, 'y', 1.2),
                                         (11, 'x', float('nan'))]:
            points = pose()
            setattr(points[index], attribute, value)
            geometry = upper_body_geometry(points)
            self.assertFalse(geometry['present'])
            self.assertEqual(geometry['shoulderConfidence'], 0.)
            for side in ('left', 'right'):
                for joint in ('Shoulder', 'Wrist'):
                    for axis in ('X', 'Y'):
                        self.assertEqual(geometry[side + joint + axis], 0.)
            json.dumps(geometry, allow_nan=False)

    def test_calibration_fixed_scale_avoids_reversing_when_shoulders_narrow(self):
        neutral = upper_body_geometry(pose(shoulder_width=.2))
        narrower = upper_body_geometry(pose(shoulder_width=.16))
        command = (narrower['leftHandVertical'] - neutral['leftHandVertical']) / neutral['shoulderWidth']
        self.assertAlmostEqual(command, 0.)
        raised = upper_body_geometry(pose(shoulder_width=.16, left_y=.59))
        self.assertGreater((raised['leftHandVertical'] - neutral['leftHandVertical']) / neutral['shoulderWidth'], 0.)

    def test_same_side_height_does_not_change_when_other_shoulder_tilts(self):
        points = pose()
        before = upper_body_geometry(points)['leftHandVertical']
        points[12].y += .04
        self.assertEqual(upper_body_geometry(points)['leftHandVertical'], before)

    def test_one_missing_wrist_does_not_hide_other_hand_or_shoulders(self):
        points = pose()
        points[15].x = 1.1
        geometry = upper_body_geometry(points)
        self.assertTrue(geometry['present'])
        self.assertEqual(geometry['leftHandConfidence'], 0.)
        self.assertEqual((geometry['leftWristX'], geometry['leftWristY']), (0., 0.))
        self.assertGreater(geometry['shoulderConfidence'], .5)
        self.assertGreater(geometry['rightHandConfidence'], .5)
        self.assertEqual((geometry['rightWristX'], geometry['rightWristY']),
                         (points[16].x, points[16].y))


class RegionalBridgeSmokeTests(unittest.TestCase):
    def test_real_log_p2_loss_keeps_fixed_roles_and_exposes_diagnostics(self):
        import bridge
        import cv2
        from mediapipe.tasks.python import vision
        from cup_detection import no_cup
        people = {1: pose(.2, .12), 2: pose(.5, .12), 3: pose(.8, .12)}
        people[2][11].visibility = .49
        boundaries = [0., 1/3, 2/3, 1.]
        diagnostics = [region_diagnostic(slot, points, boundaries, 1) for slot, points in people.items()]
        region = SimpleNamespace(detect=lambda *_:(people,list(people.values()),diagnostics),
                                 configure=lambda *_:None,close=lambda:None)
        packets, requests = [], [b'{"version":1,"performerCount":3,"upperBodyOnly":true}']
        def receive(_):
            if requests:return requests.pop(),('127.0.0.1',5055)
            raise BlockingIOError
        def send(data,address):
            try:packets.append(json.loads(data))
            except (ValueError,UnicodeError):pass
            return len(data)
        sender=SimpleNamespace(bind=lambda *_:None,setblocking=lambda *_:None,setsockopt=lambda *_:None,
                               recvfrom=receive,sendto=send,close=lambda:None)
        requested_dimensions=[]
        capture=SimpleNamespace(isOpened=lambda:True,set=lambda key,value:requested_dimensions.append((key,value)),
                                read=lambda:(True,np.zeros((240,320,3),np.uint8)),release=lambda:None)
        cups=SimpleNamespace(reset=lambda:None,for_people=lambda people,now:{slot:no_cup('mock') for slot in people},
                             status_for=lambda now:'mock',sample=lambda *_:None,draw=lambda *_:None,close=lambda:None)
        fists=SimpleNamespace(status='mock fist', inference_ms=0.,
                              detect=lambda image, people, timestamp: {}, close=lambda:None)
        segmentation=SimpleNamespace(sample=lambda *_:None,draw=lambda *_:None,close=lambda:None)
        with (patch.object(sys,'argv',['bridge.py','--model',__file__]),
              patch.object(cv2,'VideoCapture',return_value=capture),patch.object(cv2,'imshow'),
              patch.object(cv2,'waitKey',return_value=ord('q')),patch.object(cv2,'destroyAllWindows'),
              patch.object(bridge.socket,'socket',return_value=sender),
              patch.object(bridge,'RegionalPoseDetector',return_value=region),
              patch.object(bridge,'AsyncSemanticCupDetector',return_value=cups),
              patch.object(bridge,'FistGestureDetector',return_value=fists),
              patch.object(bridge,'SegmentationPreview',return_value=segmentation),
              patch.object(vision.PoseLandmarker,'create_from_options') as global_factory):
            bridge.main()
        packet=next(item for item in packets if item.get('people'))
        self.assertEqual({item['slot'] for item in packet['people']},{1,2,3})
        p2=next(item for item in packet['people'] if item['slot']==2)
        self.assertEqual(p2['confidence'][4],0.)
        self.assertFalse(p2['upperBodyGeometry']['present'])
        self.assertTrue(packet['diagnostics']['regionalTracking'])
        self.assertTrue(packet['hasCaptureAge'])
        self.assertGreaterEqual(packet['captureAgeSeconds'], 0.)
        self.assertEqual(packet['captureAgeSeconds'], packet['diagnostics']['captureAgeSeconds'])
        self.assertEqual(packet['frameSequence'], packet['diagnostics']['frameSequence'])
        self.assertEqual(packet['frameSequence'], 1)
        self.assertEqual(packet['diagnostics']['assignedPeople'],3)
        self.assertEqual(packet['diagnostics']['validPeople'],2)
        self.assertEqual((packet['diagnostics']['captureWidth'],packet['diagnostics']['captureHeight']),(320,240))
        self.assertIn('P2',packet['diagnostics']['rejectionReasons'])
        self.assertIn((cv2.CAP_PROP_FRAME_WIDTH,1280),requested_dimensions)
        self.assertIn((cv2.CAP_PROP_FRAME_HEIGHT,720),requested_dimensions)
        global_factory.assert_not_called()



class LandmarkHoldTests(unittest.TestCase):
    def test_flickering_wrist_is_held_briefly_then_released(self):
        clock = [0.]
        hold = LandmarkHold(hold_seconds=.5, clock=lambda: clock[0])
        good = pose(left_y=.55)
        self.assertAlmostEqual(hold.apply(1, good)[15].y, .55)
        flicker = pose(left_y=.9)
        flicker[15].visibility = .1  # wrist briefly unconfident
        clock[0] = .3
        held = hold.apply(1, flicker)
        self.assertAlmostEqual(held[15].y, .55)
        self.assertGreaterEqual(held[15].visibility, .5)
        clock[0] = .9
        self.assertLess(hold.apply(1, flicker)[15].visibility, .5)  # too old: no longer held

    def test_seat_stays_briefly_then_empties_when_the_person_leaves(self):
        clock = [0.]
        hold = LandmarkHold(hold_seconds=.5, clock=lambda: clock[0])
        hold.apply(2, pose())
        clock[0] = .4
        self.assertIsNotNone(hold.apply(2, None))
        clock[0] = .8
        self.assertIsNone(hold.apply(2, None))


class SoloRegionTests(unittest.TestCase):
    def test_session_accepts_solo_region_only_for_one_upper_body_person(self):
        session = TrackingSession(1, True)
        self.assertTrue(session.apply_command({'version': 1, 'performerCount': 1, 'upperBodyOnly': True, 'soloRegion': 2}))
        self.assertEqual(session.solo_region, 2)
        revision = session.revision
        self.assertFalse(session.apply_command({'version': 1, 'performerCount': 1, 'upperBodyOnly': True, 'soloRegion': 2}))
        self.assertEqual(session.revision, revision)
        self.assertTrue(session.apply_command({'version': 1, 'performerCount': 3, 'upperBodyOnly': True, 'soloRegion': 2}))
        self.assertEqual(session.solo_region, 0)
        self.assertFalse(session.apply_command({'version': 1, 'performerCount': 1, 'upperBodyOnly': True, 'soloRegion': 7}))

    def run_bridge(self, local_x):
        """Run bridge.main for one mock frame in Solo P2; local_x places the body inside the crop."""
        import cv2
        from mediapipe.tasks.python import vision
        import bridge
        from cup_detection import no_cup
        packets, widths = [], []
        commands = [b'{"version":1,"performerCount":1,"upperBodyOnly":true,"soloRegion":2}']
        def receive(_):
            if commands:
                return commands.pop(), ('127.0.0.1', 5055)
            raise BlockingIOError
        def send(data, address):
            try:
                packets.append(json.loads(data))
            except (ValueError, UnicodeError):
                pass
            return len(data)
        sender = SimpleNamespace(bind=lambda *_: None, setblocking=lambda *_: None, setsockopt=lambda *_: None,
                                 recvfrom=receive, sendto=send, close=lambda: None)
        capture = SimpleNamespace(isOpened=lambda: True, set=lambda *_: False, release=lambda: None,
                                  read=lambda: (True, np.zeros((720, 1280, 3), dtype=np.uint8)))
        cups = SimpleNamespace(reset=lambda: None, for_people=lambda people, now: {slot: no_cup() for slot in people},
                               status_for=lambda now: 'mock', sample=lambda *_: None, draw=lambda *_: None,
                               close=lambda: None)
        segmenter = SimpleNamespace(sample=lambda *_: None, draw=lambda *_: None, close=lambda: None)
        fists = SimpleNamespace(status='mock', inference_ms=0., close=lambda: None)
        worker = SimpleNamespace(status='mock', inference_ms=0., poll=lambda *_: None, submit=lambda *_: None,
                                 invalidate=lambda: None, close=lambda: None)
        def detect(image, _):
            widths.append(image.width)
            return SimpleNamespace(pose_landmarks=[pose(local_x, .3)])
        engine = SimpleNamespace(detect_for_video=detect, close=lambda: None)
        with (patch.object(sys, 'argv', ['bridge.py', '--model', __file__, '--people', '1', '--upper-body-only']),
              patch.object(cv2, 'VideoCapture', return_value=capture),
              patch.object(cv2, 'imshow'), patch.object(cv2, 'waitKey', return_value=ord('q')),
              patch.object(cv2, 'destroyAllWindows'),
              patch.object(bridge.socket, 'socket', return_value=sender),
              patch.object(bridge, 'SegmentationPreview', return_value=segmenter),
              patch.object(bridge, 'AsyncSemanticCupDetector', return_value=cups),
              patch.object(bridge, 'FistGestureDetector', return_value=fists),
              patch.object(bridge, 'FistGestureWorker', return_value=worker),
              patch.object(vision.PoseLandmarker, 'create_from_options', return_value=engine)):
            bridge.main()
        return [packet for packet in packets if 'diagnostics' in packet][0], widths

    def test_solo_p2_tracks_only_the_middle_region(self):
        packet, widths = self.run_bridge(.5)
        self.assertEqual(len(widths), 1)  # only the P2 crop was inferred
        self.assertAlmostEqual(widths[0], 1280 * (1 / 3 + 2 * .1), delta=2)  # own shoulders keep padding
        self.assertEqual([person['slot'] for person in packet['people']], [1])
        self.assertTrue(packet['diagnostics']['regionalTracking'])
        self.assertIn('Solo P2', packet['trackingStatus'])

    def test_solo_p2_ignores_a_neighbour_at_the_crop_edge(self):
        packet, _ = self.run_bridge(.03)
        self.assertEqual(packet['people'], [])
        self.assertIn('another region', packet['diagnostics']['rejectionReasons'])


if __name__ == '__main__':
    unittest.main()
