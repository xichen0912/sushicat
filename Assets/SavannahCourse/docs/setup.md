# Install and validate the savannah package

## Before importing

The baked environment was tested in a clean Unity 6000.6.3f1 URP project. Its
materials require URP. HDRP is unsupported. If a student's project uses another
pipeline, have the agent inspect the material adaptation needed; do not change
the project pipeline automatically.

Use a published starter commit that contains both `SavannahCourse/` and
`SavannahCourse.meta`, then record that commit in the student's setup notes.

For a small checkout after publication, run from the directory containing the
Unity project:

```text
git clone --filter=blob:none --sparse https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter sparse-checkout set --no-cone SavannahCourse SavannahCourse.meta
git -C DigiPhantStarter rev-parse HEAD
```

If an instructor supplied a commit, check it out before the final `rev-parse`
command. Keep `DigiPhantStarter` outside `Assets`.

## Install and inject

Copy the entire `DigiPhantStarter/SavannahCourse` folder and sibling
`DigiPhantStarter/SavannahCourse.meta` to the student's `Assets` directory. Do
not regenerate or omit `.meta` files. Let Unity compile, open and save the
elephant scene, leave Play mode, then choose **DigiPhant > Savannah > Inject
Course into Open Scene**.

Injection first makes a unique sibling `*_BeforeSavannah.unity` backup, then
adds the fixed layout-4 course to the same saved scene. Repeating injection
creates another unique backup and replaces only the recognized generated course
root. Elephant controls, camera, MediaPipe tracking, custom mappings, and
movement settings are preserved. The course stays within the original 60 × 60
stage footprint. If a finite travel radius is below 30 plus any anchor offset,
the package logs a warning only. Report whether the finish is reachable and
discuss any movement-boundary change separately with the student. Do not replace
or edit the student's MediaPipe model, bridge, existing camera, or pipeline.

Shared generated assets live in `Assets/SavannahCourse/Generated/v4`.
**DigiPhant > Savannah > Regenerate Course in Open Scene** also creates a unique
backup, then replaces an existing generated course root. Keep student additions
in separate roots. Older layout roots are recognized and replaced; their assets
remain in place. **Generate Course in Scene Copy** remains available when a
separate scene file is specifically wanted.

`Scenes/SavannahEnvironment.unity` is a preview scene with its own preview
camera and sun. Do not replace a student's scene with it. For advanced manual
use, `Prefabs/SavannahCourse.prefab` contains only scenery.

## Agent prompt

Use this prompt after copying the package:

> Read `Assets/SavannahCourse/AGENT_SETUP.md`, then inspect my Unity project,
> Unity version, render pipeline, saved/dirty scenes, current elephant movement
> root, and existing controls. Preserve my work and every imported `.meta` file.
> Do not alter my render pipeline. If this is a compatible URP project, save my
> chosen elephant scene, leave Play mode, and run **DigiPhant > Savannah >
> Inject Course into Open Scene**. Verify the unique `*_BeforeSavannah.unity`
> backup and test the same scene with my existing camera, elephant, MediaPipe
> setup, and controls. Do not replace them with the package preview camera or
> sun, or edit my MediaPipe model, bridge, or render pipeline. Do not silently
> alter movement settings; report any warning that the
> course may be unreachable. Run the two Savannah validation menu commands with
> scenes saved, then record the starter commit, imported package path, scene
> path, pipeline, results, and remaining checks in my setup notes.

## Validation and automation

In the Editor, use **DigiPhant > Savannah > Validate Shareable Environment** to
check the baked preview scene, prefab, and asset-dependency closure. Use
**DigiPhant > Savannah > Validate Generator** to check deterministic generation,
geometry budgets, and injection/backup/regeneration behaviour.

For a CI or agent run, close Unity for the project and replace
`UNITY_EDITOR`, `PROJECT_PATH`, and `LOG_PATH` below with local paths:

```text
UNITY_EDITOR -batchmode -nographics -quit -projectPath "PROJECT_PATH" -executeMethod StudentStarter.Savannah.SavannahCourseExport.ValidateImportedBundle -logFile "LOG_PATH"
```

Quote the executable path for the local shell; PowerShell requires `&` before
a quoted executable path. These placeholders are not literal commands to paste.

`ValidateImportedBundle` opens the baked preview scene, checks that its project
asset dependencies remain inside `Assets/SavannahCourse`, then runs the generator
validation. A clean URP import emitted `SAVANNAH_BUNDLE_OK` and
`SAVANNAH_IMPORT_OK`.

To inject into the named saved scene by command line, use the injection method:

```text
UNITY_EDITOR -batchmode -nographics -quit -projectPath "PROJECT_PATH" -executeMethod StudentStarter.Savannah.SavannahCourseGenerator.InjectFromCommandLine -savannahSourceScene "Assets/PATH/TO/SCENE.unity" -logFile "LOG_PATH"
```

It makes a unique backup, emits `SAVANNAH_INJECTED`, `SAVANNAH_VALIDATION_OK`,
and `SAVANNAH_INJECTION_OK` on success, and leaves the scene at the supplied
path. Use `GenerateFromCommandLine` only when a separate scene copy is wanted.
Do not start a second Unity instance on an open project. Validation and import
checks do not replace a live Play-mode test of the student's controls and camera.
