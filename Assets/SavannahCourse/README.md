# Static Savannah Course

This self-contained URP package supplies a static, low-poly savannah course. It
is a 144.28-unit folded route within a 60 × 60 footprint, with 18 acacia trees,
four decorative landmarks, a start line, and a finish arch. It adds no
colliders, runtime behaviours, interaction, animation, or sound.

| Path | Contents |
| --- | --- |
| `Generated/v4/` | Shared meshes and URP materials used by the course |
| `Prefabs/SavannahCourse.prefab` | Scenery only; suitable for an advanced manual placement |
| `Scenes/SavannahEnvironment.unity` | Independent preview scene with the course, preview camera, and preview sun |
| `Editor/` | Scene-injection generator, validation, and export tools |

For an existing elephant project, use **DigiPhant > Savannah > Inject Course
into Open Scene**. It first saves a unique sibling `*_BeforeSavannah.unity`
backup, then adds or replaces only the generated course root in the current
scene. Existing elephant controls, camera, tracking, and settings stay in that
same scene. **Generate Course in Scene Copy** remains an optional alternative.
The preview scene is only for inspecting the environment: do not copy its preview
camera or sun into a student's elephant scene.

Read [AGENT_SETUP.md](AGENT_SETUP.md) before asking an agent to install the
package. Detailed installation, command-line validation, and publishing notes
are in [docs/setup.md](docs/setup.md) and [docs/publishing.md](docs/publishing.md).

Give your agent this prompt once the starter folder is in your workspace:

> Read `SavannahCourse/AGENT_SETUP.md` in the downloaded starter repository.
> Inspect my Unity project, import the course with its metadata, and inject it
> into my currently open saved elephant scene. Preserve my controls, camera,
> MediaPipe tracking/model/bridge, and render pipeline. Verify the unique backup,
> validate the result, and tell me how to test the same scene.
