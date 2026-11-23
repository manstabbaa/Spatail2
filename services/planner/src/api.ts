/**
 * The Spatail API as the planner sees it: what a generated program may use.
 *
 * This text is part of the model's instructions and is also published in the developer guide. It must describe
 * packages/com.spatail.core/Runtime/Resources/Spatail/Script/spatail.js.txt exactly; a test checks that every
 * name mentioned here exists in that file.
 */
export const API_REFERENCE = `// Conventions: metres, Y up, +Z forward, left-handed. Vec3 = [x, y, z]. Rotations are quaternions [x, y, z, w];
// wherever a rotation is accepted, three numbers are read as Euler degrees. Colours are '#rrggbb' or a theme name.
// Flat content (text, panels) lies in its local XY plane and is read from the -Z side.

// ---- scene: draw anything -------------------------------------------------------------------------
// Every factory takes { position?, rotation?, scale?, parent?, color?, opacity?, visible?,
//   billboard?: 'none' | 'full' | 'upright',   // keep turning to face the user (upright = yaw only)
//   shade?: boolean,                            // soft lighting on solids (default true)
//   glow?: boolean,                             // see-through volume that does not hide what is behind it
//   onClick?: (node) => void } plus what is listed.
scene.group()                                    // an empty node to parent things under
scene.box({ size: [x, y, z] })
scene.sphere({ radius })
scene.cylinder({ radius, height, topRadius? })   // along local Y
scene.cone({ radius, height })                   // along local Y, point at +Y
scene.torus({ radius, tube, arc? })              // ring in the local XZ plane; arc in degrees (default 360)
scene.disc({ radius })                           // flat, in the local XZ plane, facing +Y
scene.tube({ points: Vec3[], radius, flow?: { speed, density } })   // flow animates chevrons from first point to last
scene.line({ points: Vec3[], width? })           // thin unlit tube
scene.mesh({ positions: number[], indices: number[], colors?: string[] })   // any shape you can triangulate
scene.text({ text, size?, weight?: 'medium' | 'semibold', align?: 'left' | 'center' | 'right',
             anchor?: 'top' | 'middle' | 'bottom', maxWidth? })   // -> node.width, node.height (metres, measured)
scene.panel({ width, height, radius?, fill?, border?, borderWidth? })   // rounded rectangle

// A node: position, rotation, scale, color, opacity, visible, text, fill, border are live properties.
node.set({ ... })            node.add(child)          node.remove()
node.worldPosition()         node.lookAt(point)       node.onClick(fn)      node.onHover(inside => ...)
node.spin(axis, degreesPerSecond)                     // returns a function that stops it
node.animate({ position?, scale?, opacity? }, { duration?, delay?, ease?: 'out' | 'inOut' | 'back' | 'linear', loop? })   // a promise

scene.onFrame((dt, time) => ...)     // every frame; returns a function that unsubscribes
scene.after(seconds, fn)             scene.every(seconds, fn)        await scene.wait(seconds)
scene.tween(duration, t => ..., { ease?, delay?, loop? })            scene.time

// ---- space: what the device senses, live ----------------------------------------------------------
// Nothing about the place is known in advance. These read the world as it is when you call them.
space.view                   // the camera frame you were shown with this request, or null
space.view.point(u, v)       // the world behind an image position (u, v in 0..1, origin top-left):
                             //   { position, normal, distance } or null if nothing solid is there
space.raycast(origin, direction, maxDistance?)   // -> { position, normal, distance } or null
space.floorBelow(point)      // the floor under a point (Vec3)
space.floorY                 // the device's current floor height
space.surfaces()             // flat surfaces found SO FAR: [{ id, kind: 'floor'|'wall'|'table'|..., position, rotation, size }]
                             //   may be empty; walls face into the room along their local +Z
space.nearestWall(point)     // -> { surface, position, rotation } or null
await space.find('the red valve')   // look again now and locate something by description:
                             //   [{ position, normal, distance, label }], best first, possibly empty
space.props()                // virtual objects the app placed itself (a stand-in machine):
                             //   [{ id, name, aliases, about, position, rotation, size, origin, actionPoint?, actionAxis? }]
space.prop('gate valve')     // one of them by name, or null
space.faceUser(position, maxPitchDegrees?)   // rotation that makes flat content at position readable from where the user is
space.beside(thing, gap?)    // a spot next to something, on the user's side, that does not cover it
space.topOf(thing)           // top centre of something with a size
space.freeFloorNear(point, radius)   // free floor near a point, clear of props and known walls

user.head                    // { position, forward, rotation }, live

// ---- voice, state, lifecycle ----------------------------------------------------------------------
voice.on(['next', 'continue'], () => ...)   // short spoken commands handled on the device, instantly
state                        // plain JSON that survives when this experience is replaced by a new version of itself
log(...values)               finish()       // finish() removes this experience from the room

// ---- kit: ready-made pieces (optional; all built from the primitives above) ------------------------
kit.pill(text, { color?, style?: 'solid' | 'outline', size?, position?, billboard? })   // -> group with .width .height
kit.label(text, at, { color?, style?, stem? })            // pill on a short stem at a point, facing the user
kit.pin(textOrNumber, at, { color?, stem? })              // disc on a stem; pin.head is the disc
kit.button(text, onClick, { color?, primary? })           // -> group with .width .height
kit.card({ title, body?, number?, width?, color?, position?, rotation?, footnote?,
           buttons?: [{ text, onClick, primary? }] })     // -> group with .width .height
kit.arcArrow({ position?, radius?, arc?, clockwise?, color? })   // a turning circular arrow in the local XZ plane
kit.arrow(from, to, { color?, radius? })
kit.glow(thing, { color?, inflate? })                     // pulsing see-through volume around something with a size
kit.appear(node, { delay? })                              // pop a node in

// ---- helpers ---------------------------------------------------------------------------------------
vec.add  vec.sub  vec.scale  vec.mul  vec.dot  vec.cross  vec.length  vec.distance  vec.normalize  vec.lerp  vec.flat
quat.axisAngle(axis, degrees)  quat.euler(x, y, z)  quat.mul  quat.rotate(q, v)  quat.inverse  quat.look(forward, up?)
quat.fromTo(a, b)  quat.slerp
theme.accent  theme.accentDeep  theme.accentSoft  theme.warning  theme.success  theme.ink  theme.muted  theme.surface
theme.text.small (0.018)  theme.text.body (0.022)  theme.text.label (0.028)  theme.text.title (0.03)  theme.text.large (0.045)
theme.layer                  // put text this far in front (-Z) of the panel it sits on`;
