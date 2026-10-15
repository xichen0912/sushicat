"""Run the local tracking bridge and summarize packets; save no photos or video."""
import argparse
import datetime as dt
import json
from pathlib import Path
import select
import socket
import statistics
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=float, default=25)
    parser.add_argument('--people', type=int, choices=range(1, 5), default=1)
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--upper-body-only', action='store_true')
    parser.add_argument('--no-segmentation', action='store_true')
    args = parser.parse_args()
    if not 5 <= args.seconds <= 60:
        parser.error('--seconds must be between 5 and 60')
    sockets = []
    bridge = None
    folder = ROOT / 'Logs' / 'CameraDiagnostics' / dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True, exist_ok=False)
    packets, previews, arrivals = [], 0, []
    began = time.monotonic()
    try:
        for port in (5055, 5057):
            receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sockets.append(receiver)
            receiver.bind(('127.0.0.1', port))
            receiver.setblocking(False)
        command = [str(ROOT / 'DigiPhantStarter/Tracking/.venv/bin/python'), '-u',
                   str(ROOT / 'DigiPhantStarter/Tracking/bridge.py'), '--no-window',
                   '--people', str(args.people), '--camera', str(args.camera)]
        if args.upper_body_only:
            command.append('--upper-body-only')
        if args.no_segmentation:
            command.append('--no-segmentation')
        with (folder / 'bridge.log').open('w') as log:
            bridge = subprocess.Popen(command, cwd=ROOT / 'DigiPhantStarter/Tracking',
                                      stdout=log, stderr=subprocess.STDOUT)
            next_progress = began + 5
            while time.monotonic() - began < args.seconds and bridge.poll() is None:
                ready, _, _ = select.select(sockets, [], [], .2)
                for receiver in ready:
                    for _ in range(64):
                        try:
                            data, address = receiver.recvfrom(65535)
                        except BlockingIOError:
                            break
                        if address != ('127.0.0.1', 5056):
                            continue
                        if receiver is sockets[0]:
                            frame = json.loads(data)
                            if frame.get('version') == 1 and frame.get('performerCount') == args.people:
                                packets.append(frame)
                                arrivals.append(time.monotonic() - began)
                        elif data.startswith(b'\xff\xd8'):
                            previews += 1
                if time.monotonic() >= next_progress:
                    assigned = len(packets[-1].get('people', [])) if packets else 0
                    print(f'LOCAL_CAMERA_PROGRESS: packets={len(packets)}, assigned={assigned}/{args.people}, previews={previews}', flush=True)
                    next_progress += 5
    finally:
        if bridge is not None and bridge.poll() is None:
            bridge.terminate()
            try:
                bridge.wait(timeout=5)
            except subprocess.TimeoutExpired:
                bridge.kill()
                bridge.wait(timeout=5)
        for receiver in sockets:
            receiver.close()
    intervals = [b-a for a, b in zip(arrivals, arrivals[1:])]
    people = [p for frame in packets for p in frame.get('people', []) if p.get('slot') == 1]
    required_ready = [all(c >= .5 for c in p['confidence']) for p in people]
    ranges = [max(p['values'][k] for p in people)-min(p['values'][k] for p in people)
              for k in range(6)] if people else []
    report = {
        'kind': 'actual_local_camera_diagnostic', 'photos_saved': False, 'video_recorded': False,
        'requested_people': args.people, 'upper_body_only': args.upper_body_only,
        'segmentation_enabled': not args.no_segmentation, 'duration_seconds': time.monotonic()-began,
        'pose_packet_count': len(packets), 'jpeg_preview_count': previews,
        'packets_with_full_group': sum(len(p.get('people', [])) == args.people for p in packets),
        'p1_observations': len(people), 'p1_all_six_signals_ready': sum(required_ready),
        'p1_signal_ranges': ranges, 'median_packet_interval_seconds': statistics.median(intervals) if intervals else None,
        'max_packet_interval_seconds': max(intervals) if intervals else None,
        'scope': 'Actual capture/inference and localhost packets only; no Unity calibration, three-person performance or recording claim.',
    }
    (folder / 'diagnostic.json').write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report, indent=2))
    print('DIAGNOSTIC_PATH:', folder)
    return 0 if people and previews else 2


if __name__ == '__main__':
    raise SystemExit(main())
