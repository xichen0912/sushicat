"""Reproduce unsafe legacy recovery and exercise conservative student roles.

Synthetic positions test software policy only; they cannot establish identity
recognition or prove accuracy with three real people in front of a camera.
"""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bridge import PerformerTracker
from identity_tracking import StablePerformerTracker, TrackingSession


class Clock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


class StableIdentityTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.tracker = StablePerformerTracker(3, clock=self.clock)
        self.centers = [(.2, .5), (.4, .5), (.8, .5)]

    def seed(self):
        self.assertEqual(self.tracker.assign(self.centers), {1: 0, 2: 1, 3: 2})

    def test_roles_survive_detection_order_changes(self):
        self.seed()
        self.clock.now = .1
        self.assertEqual(self.tracker.assign([(.81, .5), (.21, .5), (.41, .5)]),
                         {1: 1, 2: 2, 3: 0})
        self.assertFalse(self.tracker.reset_required)

    def test_short_dropout_holds_missing_role_without_reassigning(self):
        self.seed()
        self.clock.now = .3
        self.assertEqual(self.tracker.assign([(.21, .5), (.41, .5)]), {1: 0, 2: 1})
        self.clock.now = 1.9
        self.assertEqual(self.tracker.assign([(.82, .5), (.22, .5), (.42, .5)]),
                         {1: 1, 2: 2, 3: 0})
        self.assertFalse(self.tracker.reset_required)

    def test_prolonged_dropout_does_not_automatically_reuse_old_role(self):
        original = PerformerTracker(3)
        original.assign(self.centers)
        self.seed()
        self.clock.now = 1.5
        self.assertEqual(self.tracker.assign(self.centers[:2]), {1: 0, 2: 1})
        original.assign(self.centers[:2])
        self.clock.now = 2.01
        self.assertEqual(len(original.assign(self.centers)), 3)
        self.assertEqual(self.tracker.assign(self.centers), {})
        self.assertTrue(self.tracker.reset_required)
        self.assertIn("P3", self.tracker.reason)
        self.clock.now = 5
        self.assertEqual(self.tracker.assign(self.centers), {})

    def test_ambiguity_is_latched_instead_of_resuming_next_frame(self):
        original = PerformerTracker(3)
        original.assign(self.centers)
        self.seed()
        ambiguous = [(.3, .5), (.8, .5)]
        self.clock.now = .1
        self.assertEqual(original.assign(ambiguous), {})
        self.assertEqual(self.tracker.assign(ambiguous), {})
        self.clock.now = .2
        self.assertEqual(len(original.assign(self.centers)), 3)
        self.assertEqual(self.tracker.assign(self.centers), {})
        self.assertIn("Ambiguous", self.tracker.reason)
        self.assertEqual(self.tracker.positions, self.centers)

    def test_extra_bystander_cannot_seed_a_three_person_group(self):
        centers = [(.05, .5), (.2, .5), (.4, .5), (.8, .5)]
        original = PerformerTracker(3)
        self.assertEqual(original.assign(centers), {1: 0, 2: 1, 3: 2})
        self.assertEqual(self.tracker.assign(centers), {})
        self.assertIsNone(self.tracker.positions)
        self.assertFalse(self.tracker.reset_required)
        self.assertIn("exactly 3", self.tracker.status)
        self.seed()

    def test_extra_person_after_seeding_requires_explicit_reset(self):
        self.seed()
        self.clock.now = .1
        self.assertEqual(self.tracker.assign([(.05, .5)] + self.centers), {})
        self.assertFalse(self.tracker.reset_required)
        self.clock.now = .7
        self.assertEqual(self.tracker.assign([(.05, .5)] + self.centers), {})
        self.assertIn("Extra visible person", self.tracker.reason)
        self.assertEqual(self.tracker.assign(self.centers), {})

    def test_one_frame_extra_person_pauses_then_recovers_same_roles(self):
        self.seed()
        self.clock.now = .1
        self.assertEqual(self.tracker.assign(self.centers + [(.95, .5)]), {})
        self.assertIn('paused', self.tracker.status)
        self.clock.now = .2
        self.assertEqual(self.tracker.assign(self.centers), {1: 0, 2: 1, 3: 2})
        self.assertFalse(self.tracker.reset_required)

    def test_explicit_reset_releases_latch_and_seeds_left_to_right(self):
        self.seed()
        self.tracker.assign([(.3, .5), (.8, .5)])
        self.tracker.reset()
        self.assertIsNone(self.tracker.positions)
        self.assertFalse(self.tracker.reset_required)
        self.assertEqual(self.tracker.reason, "")
        self.clock.now = 100
        self.assertEqual(self.tracker.assign([(.8, .5), (.2, .5), (.4, .5)]),
                         {1: 1, 2: 2, 3: 0})

    def test_timeout_uses_elapsed_time_instead_of_missing_frame_count(self):
        self.seed()
        self.clock.now = .1
        for _ in range(1000):
            self.assertEqual(self.tracker.assign([]), {})
        self.assertFalse(self.tracker.reset_required)
        self.clock.now = 2.1
        self.assertEqual(self.tracker.assign([]), {})
        self.assertTrue(self.tracker.reset_required)

    def test_each_slot_gets_its_own_last_seen_time(self):
        self.seed()
        self.clock.now = 1.0
        self.assertEqual(self.tracker.assign(self.centers[:2]), {1: 0, 2: 1})
        self.clock.now = 1.9
        self.assertEqual(self.tracker.assign([self.centers[2]]), {3: 0})
        self.clock.now = 2.5
        self.assertEqual(self.tracker.assign(self.centers), {1: 0, 2: 1, 3: 2})
        self.assertFalse(self.tracker.reset_required)

    def test_four_person_seed_also_requires_exact_count(self):
        tracker = StablePerformerTracker(4, clock=self.clock)
        centers = [(.05, .5), (.25, .5), (.45, .5), (.65, .5), (.85, .5)]
        self.assertEqual(tracker.assign(centers), {})
        self.assertEqual(len(tracker.assign(centers[:4])), 4)

    def test_two_person_initial_behavior_is_preserved(self):
        centers = [(.8, .5), (.2, .5), (.5, .5)]
        for count in (2,):
            original = PerformerTracker(count)
            tracker = StablePerformerTracker(count, clock=self.clock)
            self.assertEqual(tracker.assign(centers), original.assign(centers))

    def test_solo_refuses_to_choose_leftmost_of_multiple_teammates(self):
        tracker = StablePerformerTracker(1, clock=self.clock)
        self.assertEqual(tracker.assign([(.2, .5), (.8, .5)]), {})
        self.assertIsNone(tracker.positions)
        self.assertIn('exactly 1', tracker.status)
        self.assertEqual(tracker.assign([(.8, .5)]), {1: 0})
        self.clock.now = .1
        self.assertEqual(tracker.assign([(.2, .5), (.8, .5)]), {})
        self.clock.now = .2
        self.assertEqual(tracker.assign([(.8, .5)]), {1: 0})

    def test_default_timeout_and_invalid_timeout(self):
        self.assertEqual(self.tracker.identity_timeout, 2.0)
        for timeout in (0, -1, float("nan"), float("inf")):
            with self.subTest(timeout=timeout), self.assertRaises(ValueError):
                StablePerformerTracker(3, identity_timeout=timeout)


class TrackingSessionTests(unittest.TestCase):
    def test_repeated_unity_requests_preserve_revision_and_identity(self):
        session = TrackingSession(3, True)
        session.tracker.assign([(.2, .5), (.5, .5), (.8, .5)])
        for _ in range(10):
            self.assertFalse(session.apply_command({'version': 1, 'performerCount': 3, 'upperBodyOnly': True}))
        self.assertEqual(session.revision, 1)
        self.assertIsNotNone(session.tracker.positions)

    def test_r_key_and_explicit_reset_invalidate_old_neutral(self):
        session = TrackingSession(3, True)
        session.reset_assignments()
        self.assertEqual(session.revision, 2)
        self.assertTrue(session.apply_command({'version': 1, 'performerCount': 3,
                                               'upperBodyOnly': True, 'reset': True}))
        packet = {}
        session.append_protocol(packet)
        self.assertEqual(packet['assignmentRevision'], 3)
        self.assertEqual(packet['trackingSession'], session.session_id)
        self.assertIn('trackingStatus', packet)

    def test_changed_count_mode_and_reset_increment_once(self):
        session = TrackingSession(3, False)
        self.assertTrue(session.apply_command({'version': 1, 'performerCount': 1,
                                               'upperBodyOnly': True, 'reset': True}))
        self.assertEqual((session.revision, session.count, session.upper_body_only), (2, 1, True))
        self.assertEqual(session.tracker.count, 1)

    def test_bridge_restart_cannot_reuse_session_even_if_revision_is_one(self):
        first, second = TrackingSession(3), TrackingSession(3)
        self.assertEqual((first.revision, second.revision), (1, 1))
        self.assertNotEqual(first.session_id, second.session_id)

    def test_invalid_requests_do_not_reset_or_change_roles(self):
        session = TrackingSession(3, True)
        for command in ({}, [], {'version': True, 'performerCount': 1},
                        {'version': 1, 'performerCount': True},
                        {'version': 1, 'performerCount': 5},
                        {'version': 1, 'performerCount': 1, 'upperBodyOnly': 1}):
            self.assertFalse(session.apply_command(command))
        self.assertEqual((session.count, session.upper_body_only, session.revision), (3, True, 1))


if __name__ == "__main__":
    unittest.main()
