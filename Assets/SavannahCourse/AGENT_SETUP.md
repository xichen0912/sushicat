# Agent setup: static savannah course

Open the agent at the student's Unity project root. First inspect the project
version, render pipeline, saved scenes, current elephant movement setup, and
unsaved work. The baked package was verified with Unity 6000.6.3f1 and URP. Its
materials are URP materials; HDRP is not supported. If the project is not URP,
identify and verify the material/source adaptations needed before using the
package. Preserve the project's render pipeline; compatibility outside URP has
not been tested.

Copy the complete `SavannahCourse` directory and its sibling
`SavannahCourse.meta` into the student's `Assets` directory, producing
`Assets/SavannahCourse`. Preserve every included `.meta` file and GUID. If that
destination already exists, inspect and reuse it; do not create a duplicate
import or overwrite student changes blindly.

Keep the layout-4 geometry, fixed scale, and seed `20261005` so all students get
the same course. Add no colliders, sound, animation, or interaction logic.

The normal workflow is:

1. Let Unity finish importing and compiling.
2. Save the student's chosen elephant scene and leave Play mode.
3. Run **DigiPhant > Savannah > Inject Course into Open Scene**.
4. Confirm the unique sibling `*_BeforeSavannah.unity` backup, then test the
   same scene with the student's existing controls and camera.

Injection replaces only the recognized generated course root on repeat runs; it
does not duplicate it. It preserves existing elephant/camera/MediaPipe/tracking
settings, custom mappings, and movement settings. If the generator cannot find
exactly one `DigiPhant.DigiPhantLocomotion`, select the elephant movement root
in the Hierarchy and retry. If the existing finite travel radius cannot reach
the whole course, report that to the student and discuss a separate adjustment;
do not silently alter their movement controls.

Keep student cameras, controls, scripts, and objects outside the generated
course root. If injection refuses a course root with custom components, move or
reconcile those student additions outside that root before retrying; the refusal
leaves the scene unchanged.

Do not replace or edit the student's MediaPipe model, bridge, existing camera,
or render pipeline as part of injection.

`Scenes/SavannahEnvironment.unity` is an independent environment preview, not a
replacement for the student's elephant scene. `Prefabs/SavannahCourse.prefab`
contains scenery only and is the advanced manual-placement option; it does not
bring an elephant, tracking, camera, or sun.

After import, use **DigiPhant > Savannah > Validate Shareable Environment** and
**DigiPhant > Savannah > Validate Generator** with saved scenes and Unity out of
Play mode. Record the starter commit, Unity version, render pipeline, generated
scene path, validation results, and any unperformed live Play-mode check.
