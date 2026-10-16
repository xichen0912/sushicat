using System;
using System.Linq;
using DigiPhant;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudentWork.Editor
{
    // Captured-age and frame-evidence regressions; direct packets, no webcam or Play.
    public static class OptimizedControlValidation
    {
        static int assertions, sequence;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException(message); }
        static PoseFrame Frame(float delta = 0, float scale = 1, bool geometry = false, float age = 0)
        {
            int number = ++sequence;
            return new PoseFrame {
                version = 1, performerCount = 3, upperBodyOnly = true,
                trackingSession = "optimized-control-synthetic", assignmentRevision = 1,
                hasCaptureAge = true, captureAgeSeconds = age, frameSequence = number,
                diagnostics = new CameraTrackingDiagnostics { regionalTracking = true, frameSequence = number, captureAgeSeconds = age },
                people = Enumerable.Range(1, 3).Select(slot => {
                    float width = .2f * scale, vertical = -.3f * scale + .2f * delta, center = slot / 3f - 1f / 6;
                    return new PerformerFrame {
                        slot = slot, values = new[] { geometry ? vertical / width : delta, geometry ? -.3f * scale / width : 0, 0f, 0f, 0f, 0f },
                        confidence = new[] { 1f, 1f, 0f, 0f, 1f, 1f }, hasLeftArm = true, leftArmConfidence = 1,
                        hasHeadYaw = true, headYawConfidence = 1,
                        upperBodyGeometry = !geometry ? null : new UpperBodyGeometry {
                            present = true, shoulderWidth = width, leftHandVertical = vertical, rightHandVertical = -.3f * scale,
                            leftHandConfidence = 1, rightHandConfidence = 1, shoulderConfidence = 1,
                            leftShoulderX = center + width / 2, rightShoulderX = center - width / 2,
                            leftShoulderY = .4f, rightShoulderY = .4f, leftWristX = center + width / 2,
                            leftWristY = .4f - vertical, rightWristX = center - width / 2, rightWristY = .4f + .3f * scale
                        }
                    };
                }).ToArray()
            };
        }
        static void Accept(DigiPhantController c, PoseFrame f, float now)
            => Check(c.AcceptPacket(JsonUtility.ToJson(f), now), "Valid optimized packet rejected.");
        static DigiPhantController Open()
        {
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            var c = UpperBodyCourseSceneSetup.Controller(scene);
            c.inputMode = InputMode.Camera; c.CaptureRestPose();
            c.GetComponent<DigiPhantLocomotion>().inputSmoothing = 0;
            return c;
        }
        public static void RunBatch()
        {
            assertions = sequence = 0;
            var c = Open(); var loco = c.GetComponent<DigiPhantLocomotion>();
            try
            {
                Check(loco.reverseMinimumSamples >= 3 && c.maximumPoseAgeSeconds <= .5f && c.standingScaleTolerance <= .2f,
                    "Saved scene must enable all optimized safety guards.");
                Accept(c, Frame(), 100); Check(c.CalibrateAt(100), "Neutral must calibrate.");
                Accept(c, Frame(-.95f), 100.05f);
                int fresh = c.FreshPoseRevision;
                for (int i = 0; i < 20; i++) c.ApplyControls(100.05f + i / 60f, 1 / 60f);
                Check(loco.CurrentSpeed == 0 && loco.ReversePending && loco.ReverseEvidenceSamples == 1
                    && loco.ReverseEvidenceSeconds == 0 && c.FreshPoseRevision == fresh,
                    "Repeating a single negative camera sample must never confirm reverse.");
                c.ApplyControls(100.51f, 1 / 60f);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending, "Expired captured-age input must clear pending reverse.");
                for (int i = 0; i < 4; i++) {
                    float time = 101 + i * .1f; Accept(c, Frame(-.95f), time); c.ApplyControls(time, .1f);
                    if (i < 3) Check(loco.CurrentSpeed == 0, "Distinct samples still need the full evidence time span.");
                }
                Check(loco.CurrentSpeed < 0 && loco.ReverseEvidenceSamples == 4 && loco.ReverseEvidenceSeconds >= .29f,
                    "Several fresh deliberate negative samples must permit reverse.");
                Accept(c, Frame(-.32f), 101.4f); c.ApplyControls(101.4f, .1f);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending && loco.ReverseEvidenceSamples == 0,
                    "Returning above the reverse exit threshold must clear reverse evidence.");
                Accept(c, Frame(-.95f), 101.5f); c.ApplyControls(101.5f, .1f);
                Check(loco.CurrentSpeed == 0 && loco.ReverseEvidenceSamples == 1, "Re-entering reverse must require a new hold.");

                var duplicate = Frame(1.8f); Accept(c, duplicate, 102); c.ApplyControls(102, .1f);
                fresh = c.FreshPoseRevision; int frames = c.ReceivedFrames;
                duplicate.people[0].values[0] = -1;
                Check(!c.AcceptPacket(JsonUtility.ToJson(duplicate), 102.1f) && c.FreshPoseRevision == fresh && c.ReceivedFrames == frames,
                    "A duplicate sequence must not refresh or replace controls.");
                duplicate.frameSequence--; duplicate.diagnostics.frameSequence = duplicate.frameSequence;
                Check(!c.AcceptPacket(JsonUtility.ToJson(duplicate), 102.2f), "Reordered source frames must be ignored.");
                var old = Frame(1.8f, age: .75f);
                Check(!c.AcceptPacket(JsonUtility.ToJson(old), 102.3f), "A newly received old image must remain unavailable.");
                c.ApplyControls(102.3f, .1f);
                Check(loco.CurrentSpeed == 0 && c.LatestPoseAgeSeconds(102.3f) >= .74f && c.TravelInputSafetyStatus.Contains("stale"),
                    "Late inference must pause immediately and report captured-age rejection.");
                Accept(c, Frame(), 102.4f); c.ApplyControls(102.4f, .1f);
                Check(loco.CurrentSpeed == 0 && c.TravelInputSafetyReady(102.4f), "A fresh neutral must safely recover from delayed input.");
                loco.StopAnimation();

                c = Open(); loco = c.GetComponent<DigiPhantLocomotion>();
                Accept(c, Frame(geometry: true), 200); Check(c.CalibrateAt(200), "Geometric neutral must calibrate.");
                Accept(c, Frame(scale: 1.4f, geometry: true), 200.05f); c.ApplyControls(200.05f, .05f);
                Check(loco.CurrentSpeed == 0 && c.IsCalibrated && !c.TravelInputSafetyReady(200.05f),
                    "One apparent distance jump must pause before it can reverse.");
                Accept(c, Frame(geometry: true), 200.1f); c.ApplyControls(200.1f, .05f);
                Check(c.IsCalibrated && c.TravelInputSafetyReady(200.1f), "One bad scale sample must recover at the original neutral without forced recalibration.");
                for (int i = 0; i < 4; i++) {
                    float time = 201 + i * .1f; Accept(c, Frame(scale: 1.4f, geometry: true), time); c.ApplyControls(time, .1f);
                    Check(loco.CurrentSpeed == 0, "Sustained distance scaling must never reverse the elephant.");
                }
                Check(!c.IsCalibrated && c.CalibrationResetReason.Contains("distance"), "Sustained distance change must require explicit recalibration.");
                c.BeginCalibrationCountdown(202);
                for (int i = 0; i < 7; i++) Accept(c, Frame(scale: 1.4f, geometry: true), 210.5f + i * .25f);
                c.UpdateCalibrationCountdown(212);
                Check(c.IsCalibrated && !c.CalibrationPending && Mathf.Abs(c.NeutralShoulderWidth(1) - .28f) < .001f,
                    "The new standing distance must permit stable recalibration.");
                Accept(c, Frame(scale: 1.4f, geometry: true), 212.05f); c.ApplyControls(212.05f, .05f);
                Check(c.TryReadMovement(1, Movement.LeftHandHeight, 212.05f, out float value) && Mathf.Abs(value) < .001f && loco.CurrentSpeed == 0,
                    "Recalibration at a new distance must save an actual neutral hand.");
                loco.StopAnimation();

                c = Open(); loco = c.GetComponent<DigiPhantLocomotion>();
                Accept(c, Frame(geometry: true), 300); Check(c.CalibrateAt(300), "Jump-test neutral must calibrate.");
                Accept(c, Frame(-1.5f, geometry: true), 300.05f); c.ApplyControls(300.05f, .05f);
                Check(loco.CurrentSpeed == 0 && !c.IsMovementVisible(1, Movement.LeftHandHeight, 300.05f),
                    "A high-confidence wrist jump must pause rather than become throttle.");
                for (int i = 1; i < 16; i++) c.ApplyControls(300.05f + i / 60f, 1 / 60f);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending, "Cached wrist spikes must not settle merely by waiting.");
                Accept(c, Frame(geometry: true), 300.4f); c.ApplyControls(300.4f, .05f);
                Check(c.TravelInputSafetyReady(300.4f) && loco.CurrentSpeed == 0, "Returning the hand to neutral must recover from a spike.");
                for (int i = 0; i < 7; i++) {
                    float time = 301 + i * .1f; Accept(c, Frame(-1.5f, geometry: true), time); c.ApplyControls(time, .1f);
                    if (i < 5) Check(loco.CurrentSpeed == 0, "A large real gesture needs stable fresh wrist evidence, then deliberate reverse evidence.");
                }
                Check(loco.CurrentSpeed < 0 && c.TravelInputSafetyReady(301.6f), "A stable intentional large gesture must remain usable.");
                c.SetInputMode(InputMode.TestSliders); loco.testForward = .5f;
                c.ApplyControls(302, .1f); c.ApplyControls(302.3333f, .3333f);
                Check(loco.LastAppliedDeltaSeconds == .1f && loco.LastRealDeltaSeconds > .33f
                    && Mathf.Abs(loco.ActualSpeedSigned) < Mathf.Abs(loco.CurrentSpeed),
                    "Measured movement must reflect real elapsed time when render-step movement is limited.");
                Debug.Log("OPTIMIZED_CONTROL_VALIDATION_OK: " + assertions + " assertions; distinct fresh-frame reverse, exit/re-entry, duplicates/delay guards, standing distance/recalibration, confidence-independent jumps and real displacement speed; synthetic only, no camera.");
            }
            finally { loco.StopAnimation(); EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene); }
        }
    }
}
