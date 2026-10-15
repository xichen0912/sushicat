"""Conservative student identity assignment for the local DigiPhant bridge.

Based on the nearest-position assignment in kommanderpi/studentstarter
Tracking/bridge.py (pinned commit 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f).
Positions are temporary roles, not recognition of a person's identity. Once
proximity cannot support a role safely, only an explicit reset can release it.
"""
import itertools
import math
import time
import uuid


class StablePerformerTracker:
    def __init__(self, count, max_distance=.20, identity_timeout=2.0, clock=None,
                 extra_person_grace=.5):
        if type(count) is not int or count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        if not math.isfinite(identity_timeout) or identity_timeout <= 0:
            raise ValueError("Identity timeout must be positive and finite")
        if not math.isfinite(extra_person_grace) or extra_person_grace < 0:
            raise ValueError("Extra-person grace must be nonnegative and finite")
        self.count = count
        self.max_distance = max_distance
        self.identity_timeout = identity_timeout
        self.extra_person_grace = extra_person_grace
        self._clock = time.monotonic if clock is None else clock
        self.reset()

    def reset(self):
        self.positions = None
        self._last_seen = None
        self.reset_required = False
        self.reason = ""
        self._extra_since = None
        self.status = f"Waiting for {self.count} visible people"

    def _lock(self, reason):
        self.reset_required = True
        self.reason = reason
        self.status = "RESET REQUIRED: " + reason
        return {}

    def assign(self, centers):
        if self.reset_required:
            return {}
        now = self._clock()
        # Solo means exactly one visible person, rather than choosing the
        # leftmost teammate when switching the UI to Solo P3 with everyone in shot.
        if self.count == 1 and len(centers) != 1:
            self.status = f"Solo practice needs exactly 1 visible person; found {len(centers)}"
            return {}
        if self.positions is None:
            # A fourth bystander must not replace a teammate in a three-person seed.
            if self.count >= 3 and len(centers) != self.count:
                self.status = f"Need exactly {self.count} visible people; found {len(centers)}"
                return {}
            if len(centers) < self.count:
                self.status = f"Waiting for {self.count} visible people; found {len(centers)}"
                return {}
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order]
            self._last_seen = [now] * self.count
            self.status = f"{self.count}/{self.count} assigned"
            return {slot + 1: index for slot, index in enumerate(order)}

        if self.count >= 3 and len(centers) > self.count:
            if self._extra_since is None:
                self._extra_since = now
            if now - self._extra_since >= self.extra_person_grace:
                return self._lock("Extra visible person persisted; roles cannot be confirmed")
            self.status = "Travel paused: extra visible person; keep only teammates in frame"
            return {}
        self._extra_since = None
        stale = [slot + 1 for slot, seen in enumerate(self._last_seen)
                 if now - seen > self.identity_timeout]
        if stale:
            labels = ", ".join(f"P{slot}" for slot in stale)
            return self._lock(f"{labels} tracking gap exceeded {self.identity_timeout:g}s")

        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[slot], centers[index])
                         for slot, index in enumerate(assignment) if index >= 0]
            if any(distance > self.max_distance for distance in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        if (len(candidates) > 1 and candidates[1][0] == best[0]
                and candidates[1][1] - best[1] < .015):
            return self._lock("Ambiguous proximity assignment")
        result = {slot + 1: index for slot, index in enumerate(best[2]) if index >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
            self._last_seen[slot - 1] = now
        self.status = f"{len(result)}/{self.count} assigned"
        return result


class TrackingSession:
    """Identity reset protocol shared by Unity commands and the external R key.

    Every new bridge process has a unique session; revision changes invalidate
    previous neutral poses without treating periodic repeated commands as resets.
    """
    def __init__(self, count, upper_body_only=False, **tracker_options):
        self.count = count
        self.upper_body_only = bool(upper_body_only)
        self.session_id = uuid.uuid4().hex
        self.revision = 1
        self.region_boundaries = [0., 1. / 3., 2. / 3., 1.]
        self.region_padding = .1
        # Upper-body solo role practice (1..3): track only that role's region, so
        # teammates sitting beside the practising performer are ignored. 0 = whole frame.
        self.solo_region = 0
        self._tracker_options = tracker_options
        self.tracker = StablePerformerTracker(count, **tracker_options)

    def reset_assignments(self):
        self.tracker.reset()
        self.revision += 1

    def apply_command(self, command):
        if not isinstance(command, dict) or type(command.get('version')) is not int or command['version'] != 1:
            return False
        count = command.get('performerCount')
        if type(count) is not int or not 1 <= count <= 4:
            return False
        upper = command.get('upperBodyOnly', self.upper_body_only)
        if type(upper) is not bool:
            return False
        boundaries = command.get('regionBoundaries', self.region_boundaries)
        padding = command.get('regionPadding', self.region_padding)
        try:
            from region_tracking import validate_region_configuration
            boundaries, padding = validate_region_configuration(boundaries, padding)
        except (TypeError, ValueError):
            return False
        solo_region = command.get('soloRegion', 0)
        if type(solo_region) is not int or not 0 <= solo_region <= 3:
            return False
        if count != 1 or not upper:
            solo_region = 0
        changed = (count != self.count or upper != self.upper_body_only or command.get('reset') is True
                   or boundaries != self.region_boundaries or padding != self.region_padding
                   or solo_region != self.solo_region)
        if not changed:
            return False
        if count != self.count:
            self.tracker = StablePerformerTracker(count, **self._tracker_options)
        else:
            self.tracker.reset()
        self.count, self.upper_body_only = count, upper
        self.region_boundaries, self.region_padding = boundaries, padding
        self.solo_region = solo_region
        self.revision += 1
        return True

    def append_protocol(self, packet):
        packet['assignmentRevision'] = self.revision
        packet['trackingSession'] = self.session_id
        packet['trackingStatus'] = self.tracker.status
