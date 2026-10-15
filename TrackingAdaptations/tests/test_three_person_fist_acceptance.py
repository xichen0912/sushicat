"""Synthetic three-person fist acceptance rehearsal.

This suite is a deterministic rehearsal of the camera packet boundary. It does
not claim a real-camera recognition rate: the hand points and pose landmarks are
constructed, then passed through the same association function used by the
bridge. The scenarios mirror a classroom three-person play-through:

* three roles raise/close hands at the same time;
* hands approach/cross at an ambiguous midpoint;
* one hand is hidden for one frame and returns;
* open/closed recognition jitters while a hold is being accumulated; and
* detector order changes after the role tracker has seeded P1-P3.
"""
from types import SimpleNamespace
from pathlib import Path
import sys
import unittest

ADAPTATIONS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADAPTATIONS))

from fist_detection import associate_gestures  # noqa: E402
from identity_tracking import StablePerformerTracker  # noqa: E402


def point(x, y=.45, quality=.95):
    return SimpleNamespace(x=float(x), y=float(y), visibility=quality, presence=quality)


def pose(center, shoulder_width=.16, wrist_offset=.08):
    """A compact upper-body pose with a right wrist at center + offset."""
    points = [point(center, .45) for _ in range(33)]
    half = shoulder_width / 2
    points[11] = point(center - half, .35)  # anatomical left shoulder
    points[12] = point(center + half, .35)  # anatomical right shoulder
    points[15] = point(center - wrist_offset, .48)
    points[16] = point(center + wrist_offset, .48)
    return points


def category(name, score=.92):
    return [SimpleNamespace(category_name=name, score=score)]


def hand_at(x, y=.48):
    # MediaPipe's first hand landmark is the wrist; only that point is needed
    # by associate_gestures for role matching.
    return [point(x, y)]


def people_for(centers):
    return {slot: pose(center) for slot, center in enumerate(centers, start=1)}


def right_wrist(center, offset=.08):
    return center + offset


class ThreePersonFistAcceptanceTests(unittest.TestCase):
    def test_three_simultaneous_hands_are_assigned_one_to_one(self):
        centers = (.22, .50, .78)
        people = people_for(centers)
        hands = [hand_at(right_wrist(center)) for center in centers]
        gestures = [category('Closed_Fist', .91), category('Open_Palm', .86),
                     category('Closed_Fist', .94)]

        result = associate_gestures(people, hands, gestures)

        self.assertTrue(result[1]['rightFistClosed'])
        self.assertFalse(result[2]['rightFistClosed'])
        self.assertTrue(result[3]['rightFistClosed'])
        self.assertAlmostEqual(result[1]['rightFistConfidence'], .91)
        self.assertAlmostEqual(result[2]['rightFistConfidence'], .86)
        self.assertAlmostEqual(result[3]['rightFistConfidence'], .94)
        # A signal is never silently assigned to an unrelated left hand.
        for slot in (1, 2, 3):
            self.assertEqual(result[slot]['leftFistConfidence'], 0.)

    def test_ambiguous_crossing_frame_is_rejected_then_role_recovers(self):
        # P1/P2 are deliberately close enough for a crossing hand to be within
        # the association radius of both wrists. The tie guard must pause that
        # hand instead of guessing a different person's role.
        centers = (.32, .50, .68)
        # Use a wider arm span so the midpoint is equidistant from both
        # neighbouring right wrists without being closer to a left wrist.
        people = {slot: pose(center, wrist_offset=.12)
                  for slot, center in enumerate(centers, start=1)}
        midpoint = (right_wrist(centers[0], .12) + right_wrist(centers[1], .12)) / 2
        crossing = [hand_at(right_wrist(centers[0], .12)), hand_at(midpoint),
                    hand_at(right_wrist(centers[2], .12))]
        crossing_result = associate_gestures(
            people, crossing,
            [category('Closed_Fist'), category('Closed_Fist'), category('Open_Palm')])

        self.assertTrue(crossing_result[1]['rightFistClosed'])
        self.assertEqual(crossing_result[2]['rightFistConfidence'], 0.)
        self.assertFalse(crossing_result[2]['rightFistClosed'])
        self.assertFalse(crossing_result[3]['rightFistClosed'])
        self.assertIn('No fresh', crossing_result[2]['reason'])

        # Once the hand is clear of the crossing, P2 receives its own signal
        # again; no stale empty/ambiguous frame is carried forward.
        recovered = associate_gestures(
            people,
            [hand_at(right_wrist(centers[0], .12)), hand_at(right_wrist(centers[1], .12)),
             hand_at(right_wrist(centers[2], .12))],
            [category('Closed_Fist'), category('Closed_Fist'), category('Open_Palm')])
        self.assertTrue(recovered[2]['rightFistClosed'])
        self.assertAlmostEqual(recovered[2]['rightFistConfidence'], .92)

    def test_one_frame_occlusion_clears_only_the_missing_role(self):
        centers = (.22, .50, .78)
        people = people_for(centers)
        all_closed = associate_gestures(
            people, [hand_at(right_wrist(center)) for center in centers],
            [category('Closed_Fist', .90)] * 3)
        self.assertTrue(all(all_closed[slot]['rightFistClosed'] for slot in (1, 2, 3)))

        # P2 is occluded for one frame. Fresh association output must not reuse
        # its previous closed state while P1/P3 remain valid.
        one_missing = associate_gestures(
            people, [hand_at(right_wrist(centers[0])), hand_at(right_wrist(centers[2]))],
            [category('Closed_Fist', .93), category('Closed_Fist', .89)])
        self.assertTrue(one_missing[1]['rightFistClosed'])
        self.assertFalse(one_missing[2]['rightFistClosed'])
        self.assertEqual(one_missing[2]['rightFistConfidence'], 0.)
        self.assertTrue(one_missing[3]['rightFistClosed'])

        returned_open = associate_gestures(
            people, [hand_at(right_wrist(center)) for center in centers],
            [category('Closed_Fist', .88), category('Open_Palm', .84),
             category('Closed_Fist', .90)])
        self.assertFalse(returned_open[2]['rightFistClosed'])
        self.assertAlmostEqual(returned_open[2]['rightFistConfidence'], .84)

    def test_open_closed_jitter_never_builds_a_false_consecutive_hold(self):
        centers = (.22, .50, .78)
        people = people_for(centers)
        # This is the recognizer sequence a UI hold timer would receive for P3.
        labels = ['Closed_Fist', 'Open_Palm', 'Closed_Fist', 'Open_Palm',
                  'Closed_Fist', 'Closed_Fist', 'Closed_Fist']
        observed = []
        for label in labels:
            frame = associate_gestures(
                people, [hand_at(right_wrist(center)) for center in centers],
                [category('Open_Palm'), category('Open_Palm'), category(label)])
            observed.append(frame[3]['rightFistClosed'])

        self.assertEqual(observed, [True, False, True, False, True, True, True])
        # A three-frame hold only completes at the final three stable frames;
        # the earlier alternating jitter cannot leak a held state.
        run = 0
        completions = []
        for closed in observed:
            run = run + 1 if closed else 0
            completions.append(run >= 3)
        self.assertEqual(completions, [False, False, False, False, False, False, True])

    def test_detection_order_change_keeps_three_roles_and_fist_semantics(self):
        initial_centers = [(.22, .5), (.50, .5), (.78, .5)]
        tracker = StablePerformerTracker(3)
        self.assertEqual(tracker.assign(initial_centers), {1: 0, 2: 1, 3: 2})

        # The detector returns people in right-to-left order on the next frame.
        reordered_centers = [(.78, .5), (.22, .5), (.50, .5)]
        assigned = tracker.assign(reordered_centers)
        self.assertEqual(assigned, {1: 1, 2: 2, 3: 0})
        people = {slot: pose(reordered_centers[index][0])
                  for slot, index in assigned.items()}
        hands = [hand_at(right_wrist(center[0])) for center in reordered_centers]
        gestures = [category('Closed_Fist'), category('Open_Palm'), category('Closed_Fist')]
        result = associate_gestures(people, hands, gestures)

        # Slot 1 follows the left physical performer, slot 2 the middle, and
        # slot 3 the right performer despite detector ordering changing.
        self.assertFalse(result[1]['rightFistClosed'])
        self.assertTrue(result[2]['rightFistClosed'])
        self.assertTrue(result[3]['rightFistClosed'])


if __name__ == '__main__':
    unittest.main()
