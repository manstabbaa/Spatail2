// "Measure from that to wherever I look next"
// Sensing only. The first point is whatever was at the centre of the view when you asked; the second follows
// your gaze and locks when you hold still on something for a moment (or say "set").
const aim = () => space.raycast(user.head.position, user.head.forward, 12);
const first = (space.view && space.view.point(0.5, 0.5)) || aim();
if (!first) {
  const at = vec.add(user.head.position, vec.scale(user.head.forward, 1.2));
  kit.appear(kit.pill('Look at something solid and ask again', { style: 'outline', position: at, billboard: 'full' }));
  scene.after(3, finish);
  return;
}

const a = first.position;
scene.sphere({ radius: 0.022, color: 'accent', shade: false, position: a });
const end = scene.sphere({ radius: 0.022, color: 'accent', shade: false, visible: false });
let line = null, tag = null, again = null;
let b = null, locked = false, steady = 0, steadyAt = null;

function draw(to) {
  b = to;
  if (line) line.remove();
  if (tag) tag.remove();
  line = scene.line({ points: [a, to], width: locked ? 0.008 : 0.005, color: locked ? 'accent' : 'accentDeep' });
  end.position = to;
  end.visible = true;
  const across = vec.length(vec.flat(vec.sub(to, a))), up = Math.abs(to[1] - a[1]);
  const text = `${vec.distance(a, to).toFixed(2)} m` + (locked ? `  ·  across ${across.toFixed(2)}  ·  up ${up.toFixed(2)}` : '');
  tag = kit.pill(text, { style: locked ? 'solid' : 'outline', position: vec.add(vec.lerp(a, to, 0.5), [0, 0.09, 0]), billboard: 'full' });
}

function lock() {
  if (locked || !b) return;
  locked = true;
  draw(b);
  again = kit.button('Measure again', () => { locked = false; steady = 0; again.remove(); again = null; }, { primary: false });
  again.set({ position: vec.add(vec.lerp(a, b, 0.5), [0, 0.02, 0]), billboard: 'full' });
}

// Ten times a second is plenty for a line that follows the eyes.
scene.every(0.1, () => {
  if (locked) return;
  const hit = aim();
  if (!hit || vec.distance(hit.position, a) < 0.03) return;
  if (steadyAt && vec.distance(hit.position, steadyAt) < 0.05) steady += 0.1;
  else { steady = 0; steadyAt = hit.position; }
  draw(hit.position);
  if (steady >= 1.5) lock();
});
voice.on(['set', 'mark', 'here', 'lock'], lock);
