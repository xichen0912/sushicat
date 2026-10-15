"""P3 body-local controls and conservative semantic cup association, no camera."""
import json
import math
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ADAPTATIONS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADAPTATIONS))
from interaction_support import append_interaction_fields, left_arm_feature
from cup_detection import (CupBox, MAX_AGE_SECONDS, SemanticCupDetector,
                           associate_cups, cup_boxes, no_cup)


def pose(right_wrist=(.68, .58)):
    points = [SimpleNamespace(x=.5, y=.5, visibility=.95, presence=.9)
              for _ in range(33)]
    for i, xy in {0: (.5, .2), 7: (.57, .18), 8: (.43, .18),
                  11: (.65, .35), 12: (.35, .35), 15: (.8, .6),
                  16: right_wrist}.items():
        points[i].x, points[i].y = xy
    return points


def detection(label='cup', score=.85, x=65, y=52, width=10, height=15):
    return SimpleNamespace(categories=[SimpleNamespace(category_name=label, score=score)],
                           bounding_box=SimpleNamespace(origin_x=x, origin_y=y,
                                                        width=width, height=height))


class LeftArmTests(unittest.TestCase):
    def test_optional_packet_fields_preserve_original_six_channels(self):
        values, confidence = [0., .1, .2, .3, .4, .5], [.9] * 6
        packet = {'version': 1, 'people': [dict(slot=3, values=values[:], confidence=confidence[:])]}
        cups = associate_cups({3: pose()}, [CupBox(.7, .6, .1, .15, .9)], .1)
        append_interaction_fields(packet, {3: pose()}, cups, 'ready')
        self.assertEqual(packet['people'][0]['values'], values)
        self.assertEqual(packet['people'][0]['confidence'], confidence)
        self.assertTrue(packet['people'][0]['hasLeftArm'])
        self.assertTrue(packet['people'][0]['hasHeadYaw'])
        self.assertAlmostEqual(packet['people'][0]['headYaw'], 0.)
        self.assertTrue(packet['people'][0]['cup']['detected'])
        self.assertEqual(packet['cupDetectorStatus'], 'ready')
        json.dumps(packet, allow_nan=False)

    def test_fist_geometry_can_be_carried_in_full_body_packet(self):
        packet = {'version': 1, 'upperBodyOnly': False,
                  'people': [dict(slot=3, values=[0.] * 6, confidence=[.9] * 6)]}
        fists = {3: dict(leftFistClosed=False, leftFistConfidence=0.,
                         rightFistClosed=True, rightFistConfidence=.9)}
        append_interaction_fields(packet, {3: pose()}, {3: no_cup()}, 'ready', fists, 'ready')
        geometry = packet['people'][0]['upperBodyGeometry']
        self.assertTrue(geometry['present'])
        self.assertTrue(geometry['rightFistClosed'])
        self.assertAlmostEqual(geometry['rightFistConfidence'], .9)

    def test_left_extension_does_not_depend_on_right_hand(self):
        landmarks = pose()
        before = left_arm_feature(landmarks)
        self.assertTrue(before['hasLeftArm'])
        self.assertAlmostEqual(before['leftArmExtension'], .5)
        landmarks[16].x, landmarks[16].y = .2, .05
        self.assertEqual(left_arm_feature(landmarks), before)

    def test_body_rotation_and_translation_preserve_left_extension(self):
        landmarks = pose()
        before = left_arm_feature(landmarks)['leftArmExtension']
        angle = math.radians(20)
        for point in landmarks:
            x, y = point.x - .5, point.y - .5
            point.x = .45 + x * math.cos(angle) - y * math.sin(angle)
            point.y = .45 + x * math.sin(angle) + y * math.cos(angle)
        self.assertAlmostEqual(left_arm_feature(landmarks)['leftArmExtension'], before)

    def test_outward_and_inward_signs(self):
        landmarks = pose()
        self.assertGreater(left_arm_feature(landmarks)['leftArmExtension'], 0)
        landmarks[15].x = .5
        self.assertLess(left_arm_feature(landmarks)['leftArmExtension'], 0)

    def test_missing_low_quality_nonfinite_out_of_frame_or_collapsed_shoulders(self):
        for index, attribute, value in [(15, 'visibility', .3), (12, 'presence', .2),
                                         (15, 'x', float('nan')), (15, 'y', 1.2)]:
            landmarks = pose()
            setattr(landmarks[index], attribute, value)
            self.assertFalse(left_arm_feature(landmarks)['hasLeftArm'])
        self.assertFalse(left_arm_feature([])['hasLeftArm'])
        landmarks = pose()
        landmarks[11].x, landmarks[11].y = landmarks[12].x, landmarks[12].y
        self.assertFalse(left_arm_feature(landmarks)['hasLeftArm'])


class CupAssociationTests(unittest.TestCase):
    def test_semantic_cup_only_and_normalized_bbox(self):
        boxes = cup_boxes([detection('bottle'), detection()], 100, 100)
        self.assertEqual(len(boxes), 1)
        self.assertAlmostEqual(boxes[0].x, .7)
        self.assertAlmostEqual(boxes[0].y, .595)
        self.assertAlmostEqual(boxes[0].width, .1)
        self.assertAlmostEqual(boxes[0].height, .15)
        detected = associate_cups({3: pose()}, boxes, .1)[3]
        self.assertTrue(detected['detected'])
        self.assertEqual(detected['label'], 'cup')
        self.assertEqual(detected['ageSeconds'], .1)
        self.assertIn('associated', detected['reason'])

    def test_rejection_reason_distinguishes_no_cup_low_wrist_far_and_stale(self):
        self.assertIn('No semantic', associate_cups({3: pose()}, [], .1)[3]['reason'])
        points = pose()
        points[16].visibility = .2
        box = CupBox(.7, .6, .1, .15, .9)
        self.assertIn('confidence', associate_cups({3: points}, [box], .1)[3]['reason'])
        self.assertIn('too far', associate_cups({3: pose((.2,.2))}, [box], .1)[3]['reason'])
        self.assertIn('expired', associate_cups({3: pose()}, [box], .7)[3]['reason'])

    def test_right_wrist_association_and_one_to_one_roles(self):
        boxes = [CupBox(.23, .6, .08, .13, .9), CupBox(.73, .6, .08, .13, .9)]
        people = {1: pose((.22, .6)), 3: pose((.72, .6))}
        result = associate_cups(people, boxes, .2)
        self.assertTrue(result[1]['detected'])
        self.assertTrue(result[3]['detected'])
        self.assertAlmostEqual(result[1]['x'], .23)
        self.assertAlmostEqual(result[3]['x'], .73)

    def test_cup_near_two_right_wrists_is_ambiguous(self):
        result = associate_cups({1: pose((.49, .6)), 3: pose((.51, .6))},
                                [CupBox(.5, .6, .1, .15, .9)], .2)
        self.assertFalse(result[1]['detected'])
        self.assertFalse(result[3]['detected'])
        self.assertIn('multiple right wrists', result[3]['reason'])

    def test_two_cups_near_one_wrist_are_ambiguous(self):
        result = associate_cups({3: pose((.5, .6))},
                                [CupBox(.49, .6, .08, .13, .9),
                                 CupBox(.51, .6, .08, .13, .9)], .2)
        self.assertFalse(result[3]['detected'])
        self.assertIn('Multiple cups', result[3]['reason'])

    def test_far_left_hand_only_low_wrist_quality_and_bad_scale_rejected(self):
        landmarks = pose((.2, .2))
        box = CupBox(.8, .6, .1, .15, .9)  # Close to left wrist, not right.
        self.assertFalse(associate_cups({3: landmarks}, [box], .1)[3]['detected'])
        landmarks = pose()
        landmarks[16].visibility = .1
        self.assertFalse(associate_cups({3: landmarks}, [box], .1)[3]['detected'])
        self.assertFalse(associate_cups({3: pose()}, [CupBox(.68, .58, .005, .005, .9)], .1)[3]['detected'])

    def test_stale_negative_and_nonfinite_cache_rejected(self):
        for age in [MAX_AGE_SECONDS + .01, -1., float('nan'), float('inf')]:
            self.assertFalse(associate_cups({3: pose()},
                             [CupBox(.7, .6, .1, .15, .9)], age)[3]['detected'])
        self.assertFalse(associate_cups({3: pose()}, [], .1)[3]['detected'])

    def test_malformed_labels_scores_dimensions_and_external_boxes_rejected(self):
        cases = [None, SimpleNamespace(), detection(score=float('nan')),
                 detection(score=.2), detection(score=1.1), detection(width=-2),
                 detection(x=float('inf')), detection(x=-100),
                 detection(width=100, height=100)]
        self.assertEqual(cup_boxes(cases, 100, 100), [])
        self.assertEqual(cup_boxes([detection()], 0, 100), [])
        self.assertFalse(associate_cups({3: pose()},
                         [CupBox(float('nan'), .6, .1, .1, .9)], .1)[3]['detected'])


class DetectorLifecycleTests(unittest.TestCase):
    def make_detector(self):
        engine = SimpleNamespace(
            detect_for_video=lambda image, timestamp: SimpleNamespace(detections=[detection()]),
            close=lambda: None)
        factory = SimpleNamespace(create_from_options=lambda options: engine)
        vision = SimpleNamespace(ObjectDetectorOptions=lambda **kwargs: kwargs,
                                 RunningMode=SimpleNamespace(VIDEO='VIDEO'), ObjectDetector=factory)
        tasks = SimpleNamespace(BaseOptions=lambda **kwargs: kwargs)
        with patch('cup_detection.Path.is_file', return_value=True):
            detector = SemanticCupDetector('official.tflite', True, vision, tasks)
        return detector

    def test_sample_rate_freshness_and_reset(self):
        detector = self.make_detector()
        image = SimpleNamespace(width=100, height=100)
        self.assertTrue(detector.sample(image, 1, 10.))
        self.assertFalse(detector.sample(image, 2, 10.1))
        self.assertTrue(detector.for_people({3: pose()}, 10.2)[3]['detected'])
        self.assertFalse(detector.for_people({3: pose()}, 11.)[3]['detected'])
        self.assertIn('stale', detector.status_for(11.))
        detector.reset()
        self.assertFalse(detector.for_people({3: pose()}, 10.2)[3]['detected'])
        self.assertTrue(detector.sample(image, 3, 10.2))
        detector.close()
        self.assertFalse(detector.active)

    def test_missing_model_and_errors_never_publish_cup(self):
        with patch('cup_detection.Path.is_file', return_value=False):
            detector = SemanticCupDetector('absent.tflite', True, None, None)
        self.assertIn('missing', detector.status)
        self.assertFalse(detector.for_people({3: pose()}, 1.)[3]['detected'])
        self.assertIn('missing', detector.for_people({3: pose()}, 1.)[3]['reason'])
        detector = self.make_detector()
        detector._detector.detect_for_video = lambda *_: (_ for _ in ()).throw(RuntimeError('test'))
        self.assertFalse(detector.sample(SimpleNamespace(width=100, height=100), 1, 1.))
        self.assertFalse(detector.active)
        self.assertFalse(detector.for_people({3: pose()}, 1.)[3]['detected'])


class BridgeSmokeTests(unittest.TestCase):
    def test_mock_capture_preserves_schema_resets_cache_and_sends_pose_before_inference(self):
        # Camera acquisition and pose inference are replaced with generated data.
        import cv2
        import numpy as np
        from mediapipe.tasks.python import vision
        import bridge

        events, packets, commands = [], [], [b'{"version":1,"performerCount":1,"upperBodyOnly":true,"reset":true}']
        def receive(_):
            if commands:
                return commands.pop(), ('127.0.0.1', 5055)
            raise BlockingIOError
        def send(data, address):
            try:
                packet = json.loads(data)
            except (ValueError, UnicodeError):
                return len(data)
            packets.append(packet)
            if packet.get('people'):
                events.append('pose sent')
            return len(data)
        sender = SimpleNamespace(bind=lambda *_: None, setblocking=lambda *_: None, setsockopt=lambda *_: None,
                                 recvfrom=receive, sendto=send, close=lambda: None)
        capture = SimpleNamespace(isOpened=lambda: True,
                                  set=lambda *_: False,
                                  read=lambda: (True, np.zeros((240, 320, 3), dtype=np.uint8)),
                                  release=lambda: None)
        cups = SimpleNamespace(reset=lambda: events.append('cup reset'),
                               for_people=lambda people, now: {slot: no_cup() for slot in people},
                               status_for=lambda now: 'mock ready',
                               sample=lambda *_: events.append('cup sampled'),
                               draw=lambda *_: None, close=lambda: None)
        fist_result = {}
        fists = SimpleNamespace(status='fake fist ready', inference_ms=4.,
                                detect=lambda image, people, timestamp:
                                    (events.append('fist inferred') or
                                     {slot: dict(leftFistClosed=False, leftFistConfidence=0.,
                                                 rightFistClosed=True, rightFistConfidence=.9)
                                      for slot in people}),
                                close=lambda: None)
        # The bridge owns the real recognizer through a worker.  Use a tiny
        # deterministic worker double here so this schema test does not race
        # a background thread while still checking pose-before-submit order.
        worker = SimpleNamespace(status='fake fist ready', inference_ms=4.,
                                 poll=lambda frame, revision, slots, now: fist_result.pop('value', None),
                                 submit=lambda image, people, timestamp, frame, revision, slots:
                                     (events.append('fist inferred') or
                                      fist_result.update(value={slot: dict(leftFistClosed=False,
                                                                            leftFistConfidence=0.,
                                                                            rightFistClosed=True,
                                                                            rightFistConfidence=.9)
                                                                 for slot in people})),
                                 invalidate=lambda: (events.append('fist invalidated'),
                                                     fist_result.clear()), close=lambda: None)
        segmenter = SimpleNamespace(sample=lambda *_: None, draw=lambda *_: None, close=lambda: None)
        clock = [10.]
        def delayed_pose(*_):
            clock[0] += .75  # A slow image must retain its capture age at delivery.
            return SimpleNamespace(pose_landmarks=[pose()])
        engine = SimpleNamespace(detect_for_video=delayed_pose)
        context = unittest.mock.MagicMock()
        context.__enter__.return_value = engine
        with (patch.object(sys, 'argv', ['bridge.py', '--model', __file__]),
              patch.object(cv2, 'VideoCapture', return_value=capture),
              patch.object(cv2, 'imshow'), patch.object(cv2, 'waitKey', side_effect=[0, ord('r'), ord('q')]),
              patch.object(cv2, 'destroyAllWindows'),
              patch.object(bridge.socket, 'socket', return_value=sender),
              patch.object(bridge, 'SegmentationPreview', return_value=segmenter),
              patch.object(bridge, 'AsyncSemanticCupDetector', return_value=cups),
              patch.object(bridge, 'FistGestureDetector', return_value=fists),
              patch.object(bridge, 'FistGestureWorker', return_value=worker),
              patch.object(bridge.time, 'monotonic', side_effect=lambda: clock[0]),
              patch.object(bridge.time, 'monotonic_ns', side_effect=lambda: int(clock[0] * 1_000_000_000)),
              patch.object(vision.PoseLandmarker, 'create_from_options', return_value=context)):
            bridge.main()
        self.assertEqual(events.count('cup reset'), 2)  # command reset plus explicit R identity reset
        self.assertLess(events.index('pose sent'), events.index('fist inferred'))
        self.assertLess(events.index('pose sent'), events.index('cup sampled'))
        self.assertGreaterEqual(len(packets), 2)
        packet = packets[0]
        self.assertEqual(packet['performerCount'], 1)
        self.assertTrue(packet['upperBodyOnly'])
        self.assertEqual(len(packet['people'][0]['values']), 6)
        self.assertEqual(len(packet['people'][0]['confidence']), 6)
        self.assertTrue(packet['people'][0]['hasLeftArm'])
        self.assertTrue(packet['people'][0]['hasHeadYaw'])
        self.assertAlmostEqual(packet['people'][0]['headYaw'], 0.)
        self.assertEqual(packet['people'][0]['headYawConfidence'], .9)
        self.assertFalse(packet['people'][0]['cup']['detected'])
        self.assertEqual(packet['cupDetectorStatus'], 'mock ready')
        self.assertEqual(packet['assignmentRevision'], 2)
        self.assertEqual(len(packet['trackingSession']), 32)
        self.assertEqual(packet['trackingStatus'], '1/1 assigned')
        self.assertGreaterEqual(events.count('fist invalidated'), 2)
        self.assertTrue(packet['hasCaptureAge'])
        self.assertEqual(packet['frameSequence'], 1)
        self.assertAlmostEqual(packet['captureAgeSeconds'], .75)
        self.assertAlmostEqual(packet['diagnostics']['captureAgeSeconds'], .75)
        self.assertAlmostEqual(packet['diagnostics']['poseInferenceMs'], 750.)
        geometry = packet['people'][0]['upperBodyGeometry']
        self.assertAlmostEqual(geometry['leftShoulderX'], .65)
        self.assertAlmostEqual(geometry['rightWristY'], .58)
        self.assertAlmostEqual(geometry['shoulderConfidence'], .9)
        # The first pose packet is sent without waiting for hand inference;
        # its result is carried by the following frame only.
        self.assertFalse(geometry['rightFistClosed'])
        # The deterministic worker double makes the next-packet hand cache
        # observable without making production code wait for a worker thread.
        self.assertTrue(packets[1]['people'][0]['upperBodyGeometry']['rightFistClosed'])


if __name__ == '__main__':
    unittest.main()
