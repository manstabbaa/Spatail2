/* Spatail Space Lab: the page around the lab engine (spatail-lab.js).
 *
 *   SpatailLabUI.mount(rootElement, { library, data?, examples?, plannerUrl?, space?, seed?, compact? })
 *
 * Builds everything inside rootElement and scopes its styles under .sl, so it can be dropped into another page.
 * Colours follow the host page's CSS variables when present (--bg, --surface, --ink, --muted, --line, --accent,
 * --accent-ink, --accent-soft, --on-accent, --warn, --code-bg) and fall back to Spatail's own.
 */
(function () {
  'use strict';

  var CSS = [
    '.sl{--sl-bg:var(--bg,#fff);--sl-surface:var(--surface,#fff);--sl-ink:var(--ink,#17161d);--sl-muted:var(--muted,#6b697d);--sl-line:var(--line,#e5e2f4);',
    '--sl-accent:var(--accent,#6c5ce7);--sl-accent-ink:var(--accent-ink,#5546d6);--sl-accent-soft:var(--accent-soft,#ece9fc);--sl-on-accent:var(--on-accent,#fff);',
    '--sl-warn:var(--warn,#c75a17);--sl-code:var(--code-bg,#f4f3fa);--sl-mono:var(--mono,"JetBrains Mono",ui-monospace,Menlo,Consolas,monospace);',
    'color:var(--sl-ink);font:14px/1.5 var(--body,Inter,system-ui,sans-serif);display:grid;gap:12px;min-width:0}',
    '.sl *{box-sizing:border-box}',
    '.sl-bar{display:flex;flex-wrap:wrap;gap:8px;align-items:center}',
    '.sl-seg{display:inline-flex;background:var(--sl-surface);border:1px solid var(--sl-line);border-radius:999px;padding:2px;max-width:100%;overflow-x:auto}',
    '.sl-seg button{border:0;background:none;color:var(--sl-muted);font:500 13px/1 inherit;font-family:inherit;padding:8px 12px;border-radius:999px;cursor:pointer;white-space:nowrap}',
    '.sl-seg button[aria-pressed="true"]{background:var(--sl-accent-soft);color:var(--sl-accent-ink)}',
    '.sl-btn{border:1px solid var(--sl-line);background:var(--sl-surface);color:var(--sl-accent-ink);font:600 13px/1 inherit;font-family:inherit;padding:9px 13px;border-radius:999px;cursor:pointer;white-space:nowrap}',
    '.sl-btn:hover{border-color:var(--sl-accent)}',
    '.sl-btn.primary{background:var(--sl-accent);border-color:var(--sl-accent);color:var(--sl-on-accent)}',
    '.sl-btn.small{padding:6px 10px;font-size:12px}',
    '.sl-note{color:var(--sl-muted);font-size:13px}',
    '.sl-seed{width:86px;border:1px solid var(--sl-line);background:var(--sl-surface);color:var(--sl-ink);font:500 13px/1 var(--sl-mono);padding:8px 10px;border-radius:999px}',
    '.sl-main{display:grid;grid-template-columns:minmax(0,1.7fr) minmax(0,1fr);gap:14px;align-items:stretch}',
    '.sl.compact .sl-main{grid-template-columns:minmax(0,1fr)}',
    '.sl.compact .sl-view{aspect-ratio:auto;height:clamp(300px,62vh,720px)}',
    '@media (max-width:900px){.sl-main{grid-template-columns:minmax(0,1fr)}}',
    '.sl-stage{position:relative;background:var(--sl-surface);border:1px solid var(--sl-line);border-radius:18px;overflow:hidden;display:flex;flex-direction:column;min-width:0}',
    '.sl-viewwrap{position:relative}',
    '.sl-view{position:relative;width:100%;aspect-ratio:16/10;min-height:300px}',
    '.sl-hint{position:absolute;left:10px;top:10px;font-size:11.5px;color:var(--sl-muted);background:color-mix(in srgb,var(--sl-surface) 82%,transparent);padding:4px 9px;border-radius:999px;pointer-events:none;max-width:calc(100% - 20px)}',
    '.sl-found{position:absolute;right:10px;top:10px;font-size:11.5px;font-weight:600;color:var(--sl-accent-ink);background:color-mix(in srgb,var(--sl-surface) 86%,transparent);border:1px solid var(--sl-line);padding:4px 9px;border-radius:999px;pointer-events:none}',
    '.sl-pad{position:absolute;right:10px;bottom:10px;display:grid;grid-template-columns:repeat(3,38px);grid-template-rows:repeat(2,38px);gap:4px}',
    '.sl-pad button{border:1px solid var(--sl-line);background:color-mix(in srgb,var(--sl-surface) 86%,transparent);color:var(--sl-accent-ink);border-radius:10px;font:600 15px/1 inherit;cursor:pointer;touch-action:none;user-select:none;-webkit-user-select:none}',
    '.sl-pad button:active{background:var(--sl-accent-soft)}',
    '.sl-console{border-top:1px solid var(--sl-line);padding:12px;display:grid;grid-template-columns:minmax(0,1fr);gap:10px}',
    '.sl-prompt{display:flex;align-items:center;gap:10px;background:var(--sl-bg);border:1px solid var(--sl-line);border-radius:999px;padding:6px 6px 6px 8px}',
    '.sl-prompt:focus-within{border-color:var(--sl-accent)}',
    '.sl-mic{flex:none;width:32px;height:32px;border-radius:50%;background:var(--sl-accent);display:grid;place-items:center;color:var(--sl-on-accent)}',
    '.sl-mic svg{width:15px;height:15px}',
    '.sl-prompt input{flex:1;min-width:0;border:0;outline:0;background:none;color:var(--sl-ink);font:500 15px/1.2 inherit;font-family:inherit;padding:6px 0}',
    '.sl-prompt input::placeholder{color:var(--sl-muted)}',
    '.sl-built{flex:none;background:var(--sl-accent);color:var(--sl-on-accent);font-weight:600;font-size:12px;padding:7px 12px;border-radius:999px;white-space:nowrap;max-width:45%;overflow:hidden;text-overflow:ellipsis}',
    '.sl-reply{font-size:14px;min-height:1.5em;padding:0 6px}',
    '.sl-chips{display:flex;gap:8px;overflow-x:auto;padding:2px 2px 4px;scrollbar-width:thin}',
    '.sl-chip{flex:none;border:1px solid var(--sl-line);background:var(--sl-bg);color:var(--sl-accent-ink);font:500 13px/1 inherit;font-family:inherit;padding:9px 13px;border-radius:999px;cursor:pointer;white-space:nowrap}',
    '.sl-chip:hover{border-color:var(--sl-accent);background:var(--sl-accent-soft)}',
    '.sl-chip small{display:block;font:500 10px/1 inherit;letter-spacing:.06em;text-transform:uppercase;color:var(--sl-muted);margin-bottom:4px}',
    '.sl-side{background:var(--sl-surface);border:1px solid var(--sl-line);border-radius:18px;padding:14px;display:grid;gap:12px;align-content:start;min-width:0}',
    '.sl-side h3{margin:0;font:600 12px/1 inherit;letter-spacing:.1em;text-transform:uppercase;color:var(--sl-muted)}',
    '.sl-url{width:100%;border:1px solid var(--sl-line);background:var(--sl-bg);color:var(--sl-ink);font:500 12.5px/1 var(--sl-mono);padding:9px 10px;border-radius:10px}',
    '.sl-list{display:grid;gap:6px}',
    '.sl-exp{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:2px 8px;align-items:center;border:1px solid var(--sl-line);background:var(--sl-bg);border-radius:12px;padding:8px 10px;cursor:pointer;text-align:left;color:inherit;font:inherit;width:100%}',
    '.sl-exp[aria-current="true"]{border-color:var(--sl-accent);background:var(--sl-accent-soft)}',
    '.sl-exp b{font-weight:600;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.sl-exp span{grid-column:1/-1;color:var(--sl-muted);font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.sl-exp .sl-x{border:0;background:var(--sl-surface);color:var(--sl-accent-ink);border-radius:999px;font:600 11px/1 inherit;padding:5px 9px;cursor:pointer}',
    '.sl-code{width:100%;min-height:240px;resize:vertical;border:1px solid var(--sl-line);background:var(--sl-code);color:var(--sl-ink);font:12px/1.55 var(--sl-mono);padding:10px 12px;border-radius:12px;white-space:pre;overflow:auto;tab-size:2}',
    '.sl-log{font:12px/1.5 var(--sl-mono);color:var(--sl-muted);background:var(--sl-code);border-radius:10px;padding:8px 10px;max-height:120px;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere}',
    '.sl-log .err{color:var(--sl-warn)}',
    '.sl-row{display:flex;flex-wrap:wrap;gap:8px;align-items:center}'
  ].join('\n');

  var FILM_ORDER = ['session', 'flow', 'faults', 'checklist', 'dashboard', 'walkthrough'];   // same order DemoBuilder checks

  function normalize(s) { return String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim(); }
  function hasWord(text, word) { var t = ' ' + text + ' ', w = normalize(word); return w.length > 0 && (t.indexOf(' ' + w + ' ') >= 0 || t.indexOf(' ' + w + 's ') >= 0); }
  function hasAny(text, words) { for (var i = 0; i < words.length; i++) if (hasWord(text, words[i])) return true; return false; }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }

  function currentTheme() {
    var explicit = document.documentElement.getAttribute('data-theme');
    if (explicit === 'dark' || explicit === 'light') return explicit;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function mount(root, options) {
    options = options || {};
    var data = options.data || window.SPATAIL_DATA;
    var examples = options.examples || window.SPATAIL_LAB_EXAMPLES || [];
    if (!document.getElementById('sl-style')) {
      var style = document.createElement('style');
      style.id = 'sl-style';
      style.textContent = CSS;
      document.head.appendChild(style);
    }
    var uid = 'sl' + Math.random().toString(36).slice(2, 7);
    root.classList.add('sl');
    if (options.compact) root.classList.add('compact');
    root.innerHTML = [
      '<div class="sl-bar">',
      '  <div class="sl-seg" role="group" aria-label="Space" data-role="spaces"></div>',
      '  <input class="sl-seed" data-role="seed" id="' + uid + '-seed" inputmode="numeric" aria-label="Layout seed" title="Layout seed">',
      '  <button type="button" class="sl-btn" data-role="shuffle">New layout</button>',
      '  <div class="sl-seg" role="group" aria-label="Camera">',
      '    <button type="button" data-camera="first" aria-pressed="true">First person</button>',
      '    <button type="button" data-camera="orbit" aria-pressed="false">Overview</button>',
      '  </div>',
      '</div>',
      '<p class="sl-note" data-role="about"></p>',
      '<div class="sl-main">',
      '  <div class="sl-stage">',
      '    <div class="sl-viewwrap">',
      '    <div class="sl-view" data-role="view"></div>',
      '    <span class="sl-hint">Drag to look. W A S D or the arrows to walk.</span>',
      '    <span class="sl-found" data-role="found">No surfaces found yet</span>',
      '    <div class="sl-pad" aria-label="Walk">',
      '      <button type="button" data-move="tl" aria-label="Turn left">&#8630;</button><button type="button" data-move="f" aria-label="Forward">&#8593;</button><button type="button" data-move="tr" aria-label="Turn right">&#8631;</button>',
      '      <button type="button" data-move="l" aria-label="Step left">&#8592;</button><button type="button" data-move="b" aria-label="Back">&#8595;</button><button type="button" data-move="r" aria-label="Step right">&#8594;</button>',
      '    </div>',
      '    </div>',
      '    <div class="sl-console">',
      '      <form class="sl-prompt" data-role="form" autocomplete="off">',
      '        <span class="sl-mic" aria-hidden="true"><svg viewBox="0 0 24 24"><path fill="currentColor" d="M12 15a3.5 3.5 0 0 0 3.5-3.5v-6a3.5 3.5 0 0 0-7 0v6A3.5 3.5 0 0 0 12 15zm6.5-3.5a1 1 0 1 0-2 0 4.5 4.5 0 0 1-9 0 1 1 0 1 0-2 0 6.5 6.5 0 0 0 5.5 6.42V20H9a1 1 0 1 0 0 2h6a1 1 0 1 0 0-2h-2v-2.08a6.5 6.5 0 0 0 5.5-6.42z"/></svg></span>',
      '        <input type="text" data-role="prompt" id="' + uid + '-prompt" placeholder="Just say what you need" aria-label="Request" enterkeyhint="send">',
      '        <span class="sl-built" data-role="built" hidden></span>',
      '        <button class="sl-btn primary" type="submit">Build</button>',
      '      </form>',
      '      <div class="sl-reply" data-role="reply" aria-live="polite"></div>',
      '      <div class="sl-chips" data-role="chips" aria-label="Suggested requests"></div>',
      '    </div>',
      '  </div>',
      '  <aside class="sl-side">',
      '    <h3>Who writes the program</h3>',
      '    <div class="sl-seg" role="group" aria-label="Builder">',
      '      <button type="button" data-builder="recorded" aria-pressed="true">Recorded</button>',
      '      <button type="button" data-builder="planner" aria-pressed="false">Planner (Claude)</button>',
      '    </div>',
      '    <p class="sl-note" data-role="builder-note"></p>',
      '    <input class="sl-url" data-role="url" id="' + uid + '-url" aria-label="Planner service address" placeholder="http://localhost:8787" hidden>',
      '    <h3>Running in the space</h3>',
      '    <div class="sl-list" data-role="list"></div>',
      '    <h3 data-role="code-title">Program</h3>',
      '    <textarea class="sl-code" data-role="code" id="' + uid + '-code" spellcheck="false" aria-label="Program source" placeholder="Run a request, then read or change the program it produced here."></textarea>',
      '    <div class="sl-row">',
      '      <button type="button" class="sl-btn primary" data-role="run">Run this program</button>',
      '      <button type="button" class="sl-btn" data-role="episode">Save episode</button>',
      '    </div>',
      '    <div class="sl-log" data-role="log" hidden></div>',
      '  </aside>',
      '</div>'
    ].join('\n');

    var $ = function (role) { return root.querySelector('[data-role="' + role + '"]'); };
    var view = $('view'), promptInput = $('prompt'), codeBox = $('code'), urlBox = $('url'), seedBox = $('seed');

    var lab;
    try {
      lab = window.SpatailLab.create(view, { data: data, library: options.library, theme: currentTheme(), space: options.space || 'Workshop', seed: options.seed });
    } catch (e) {
      view.innerHTML = '<p class="sl-note" style="padding:24px">The lab could not start in this browser (' + esc(e.message) + ').</p>';
      return null;
    }

    var builder = 'recorded', selected = null, lastEpisode = null, foundCount = 0;
    var defaultUrl = options.plannerUrl !== undefined ? options.plannerUrl : (/^https?:/.test(location.origin) ? location.origin : 'http://localhost:8787');
    urlBox.value = defaultUrl;

    // ---- small view helpers ----------------------------------------------------------------------

    function say(html, chip) {
      $('reply').innerHTML = html;
      var built = $('built');
      if (chip) { built.textContent = 'Built ' + chip; built.hidden = false; } else built.hidden = true;
    }

    function renderSpaces() {
      var now = lab.space();
      $('spaces').innerHTML = lab.spaces().map(function (s) {
        return '<button type="button" data-space="' + esc(s.name) + '" aria-pressed="' + (s.name === now.name) + '">' + esc(s.name) + '</button>';
      }).join('');
      var def = lab.spaces().filter(function (s) { return s.name === now.name; })[0];
      $('about').textContent = def.about + (def.seeded ? ' Layout ' + now.seed + ' is generated; nothing in it was scanned or labelled ahead of time.' : '');
      seedBox.value = def.seeded ? now.seed : '';
      seedBox.disabled = !def.seeded;
      $('shuffle').disabled = !def.seeded;
      foundCount = 0;
      $('found').textContent = 'No surfaces found yet';
    }

    function renderChips() {
      var now = lab.space(), html = '';
      var running = lab.experiences();
      if (running.some(function (e) { return e.id === 'walkthrough'; })) html += '<button type="button" class="sl-chip" data-say="next">Next</button><button type="button" class="sl-chip" data-say="back">Back</button>';
      if (now.props > 0) data.programs.forEach(function (p) { html += '<button type="button" class="sl-chip" data-prompt="' + esc(p.prompt) + '">' + esc(p.prompt) + '</button>'; });
      examples.forEach(function (x) { html += '<button type="button" class="sl-chip" data-prompt="' + esc(x.prompt) + '"><small>Hand-written example</small>' + esc(x.prompt) + '</button>'; });
      if (running.length) html += '<button type="button" class="sl-chip" data-prompt="Clear everything">Clear everything</button>';
      $('chips').innerHTML = html;
    }

    function renderBuilder() {
      Array.prototype.forEach.call(root.querySelectorAll('[data-builder]'), function (b) { b.setAttribute('aria-pressed', String(b.dataset.builder === builder)); });
      urlBox.hidden = builder !== 'planner';
      $('builder-note').textContent = builder === 'planner'
        ? 'Each request sends what the camera sees right now, depth samples and the surfaces found so far to the planner service, and runs the program Claude writes back.'
        : (lab.space().props > 0
          ? 'Replays programs written ahead of time for the stand-in pump, plus a few hand-written examples that use sensing only.'
          : 'Only the hand-written sensing examples work here: the film programs need the stand-in pump in the Workshop. Use the planner for anything else.');
    }

    function renderList() {
      var list = lab.experiences();
      if (selected && !list.some(function (e) { return e.id === selected; })) selected = null;
      $('list').innerHTML = list.length ? list.map(function (e) {
        return '<div class="sl-exp" role="button" tabindex="0" data-select="' + esc(e.id) + '" aria-current="' + (e.id === selected) + '"><b>' + esc(e.title) + '</b>' +
          '<button type="button" class="sl-x" data-stop="' + esc(e.id) + '">Stop</button><span>' + e.nodeCount + ' nodes &middot; ' + esc(e.prompt || e.id) + '</span></div>';
      }).join('') : '<p class="sl-note">Nothing yet. Every request becomes a program; it will show up here while it runs.</p>';
      var exp = list.filter(function (e) { return e.id === selected; })[0];
      var log = $('log');
      if (exp && exp.log.length) {
        log.hidden = false;
        log.innerHTML = exp.log.slice(-8).map(function (l) { return '<div' + (l.charAt(0) === '!' ? ' class="err"' : '') + '>' + esc(l) + '</div>'; }).join('');
      } else if (!lastError) log.hidden = true;
    }

    var lastError = null;
    function showError(title, message) {
      lastError = message;
      var log = $('log');
      log.hidden = false;
      log.innerHTML = '<div class="err">' + esc(title) + '</div><div class="err">' + esc(message) + '</div>';
    }

    function select(id, loadCode) {
      selected = id;
      var exp = lab.experiences().filter(function (e) { return e.id === id; })[0];
      if (exp && loadCode !== false) {
        codeBox.value = exp.code;
        codeBox.dataset.id = exp.id; codeBox.dataset.title = exp.title; codeBox.dataset.prompt = exp.prompt;
        $('code-title').textContent = 'Program: ' + exp.title;
      }
      renderList();
    }

    // ---- running things ---------------------------------------------------------------------------

    function start(spec, viewFrame) {
      lastError = null;
      $('log').hidden = true;
      var result = lab.run({ id: spec.id, title: spec.title, prompt: spec.prompt, code: spec.code, view: viewFrame });
      return result.settled.then(function (settled) {
        if (settled.error) showError('The program stopped with an error', settled.error);
        else select(spec.id);
        renderList(); renderChips();
        return settled;
      });
    }

    function recorded(prompt) {
      var p = normalize(prompt), running = lab.experiences();
      if (running.length && (p === 'clear' || p === 'reset' || hasAny(p, ['clear everything', 'clear all', 'remove everything', 'start over']))) {
        lab.stopAll(); say('Cleared.', ''); return Promise.resolve();
      }
      if (hasAny(p, ['remove', 'hide', 'delete', 'dismiss', 'get rid of'])) {
        for (var i = running.length - 1; i >= 0; i--) {
          var words = wordsFor(running[i].id);
          if (hasWord(p, running[i].title) || hasAny(p, words)) { lab.stop(running[i].id); say('Removed the ' + esc(running[i].title.toLowerCase()) + '.', ''); return Promise.resolve(); }
        }
      }
      var pick = null;
      if (lab.space().props > 0) FILM_ORDER.some(function (id) { var prog = data.programs.filter(function (x) { return x.id === id; })[0]; if (prog && hasAny(p, prog.words)) { pick = prog; return true; } return false; });
      if (!pick) examples.some(function (x) { if (hasAny(p, x.words)) { pick = x; return true; } return false; });
      // An exact chip always wins, whatever its words.
      data.programs.concat(examples).forEach(function (x) { if (normalize(x.prompt) === p && (lab.space().props > 0 || examples.indexOf(x) >= 0)) pick = x; });
      if (!pick) {
        say(lab.space().props > 0
          ? 'There is no recording for that. The recorded builder replays the six film requests and the sensing examples; the planner writes a program for anything.'
          : 'There is no recording for that here. This space has no stand-in pump, so only the sensing examples replay; the planner writes a program for anything.', '');
        return Promise.resolve();
      }
      var shot = lab.capture();
      lastEpisode = { request: null, result: { id: pick.id, title: pick.title, reply: pick.reply, code: pick.code, builder: 'recorded' } };
      return start({ id: pick.id, title: pick.title, prompt: prompt, code: pick.code }, shot.view).then(function (settled) {
        if (!settled.error) say(esc(pick.reply), pick.title);
      });
    }

    function wordsFor(id) {
      var all = data.programs.concat(examples);
      for (var i = 0; i < all.length; i++) if (all[i].id === id) return all[i].words || [];
      return [];
    }

    function plannerBase() { return String(urlBox.value || '').trim().replace(/\/+$/, ''); }

    function post(path, body) {
      return fetch(plannerBase() + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
        .then(function (r) { if (!r.ok) throw new Error('The planner answered ' + r.status + '.'); return r.json(); });
    }

    function unreachable(e) {
      say('Could not reach a planner at ' + esc(plannerBase() || '(no address)') + '. The published guide cannot call out to one; run the planner service on your machine and open the lab from it. <span class="sl-note">(' + esc(e.message) + ')</span>', '');
    }

    function planner(prompt) {
      var request = lab.buildRequest(prompt), shot = lab.lastCapture();
      say('<span class="sl-note">Sent the view, ' + request.surfaces.length + ' surface' + (request.surfaces.length === 1 ? '' : 's') + ' and ' + request.view.depth.metres.length + ' depth samples to the planner...</span>', '');
      return post('/v1/build', request).then(function (result) {
        return apply(prompt, request, result, shot, true);
      }, unreachable);
    }

    function apply(prompt, request, result, shot, mayRepair) {
      if (result.clear) lab.stopAll();
      (result.remove || []).forEach(function (id) { lab.stop(id); });
      lastEpisode = { request: request, result: result };
      if (!result.code) { say(esc(result.reply || result.error || 'Nothing was built.'), ''); renderList(); renderChips(); return Promise.resolve(); }
      var id = result.id || 'experience';
      return start({ id: id, title: result.title || 'Experience', prompt: prompt, code: result.code }, shot.view).then(function (settled) {
        if (!settled.error) { say(esc(result.reply || ''), result.title || ''); return undefined; }
        if (!mayRepair) { say('The planner built something for that but it did not run. ' + esc(settled.error.split('\n')[0]), ''); return undefined; }
        // One chance to fix its own program, with the error it produced.
        var repair = lab.buildRequest(prompt, { code: result.code, error: settled.error });
        return post('/v1/build', repair).then(function (fixed) { return apply(prompt, repair, fixed, lab.lastCapture(), false); }, unreachable);
      });
    }

    // space.find in planner mode: ask the service where it is in the current frame, then cast those image
    // positions into the world.
    function plannerFinder(query) {
      var shot = lab.capture();
      return post('/v1/locate', { query: query, image: shot.imageDataUrl.replace(/^data:image\/jpeg;base64,/, '') }).then(function (answer) {
        var hits = [];
        (answer.points || []).forEach(function (pt) {
          var hit = lab.viewPoint(shot.view, +pt.u, +pt.v);
          if (hit) { hit.label = pt.label || query; hits.push(hit); }
        });
        return hits;
      }, function () { return []; });
    }

    function submit(prompt) {
      prompt = String(prompt || '').trim();
      if (!prompt) return Promise.resolve();
      promptInput.value = '';
      // Short commands belong to whatever is running and never go to a builder.
      if (lab.say(prompt)) { say('', ''); renderList(); renderChips(); return Promise.resolve(); }
      var done = builder === 'planner' ? planner(prompt) : recorded(prompt);
      return Promise.resolve(done).then(function () { renderList(); renderChips(); });
    }

    function saveEpisode() {
      var episode = {
        savedAt: new Date().toISOString(), space: lab.space(), user: lab.userPose(),
        request: (lastEpisode && lastEpisode.request) || lab.buildRequest(codeBox.dataset.prompt || ''),
        result: (lastEpisode && lastEpisode.result) || null,
        program: codeBox.value
      };
      var text = JSON.stringify(episode);
      var fallback = function () {
        // Outside the planner service there is nowhere to post it; hand the file to the browser instead.
        try {
          var a = document.createElement('a');
          a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
          a.download = 'spatail-episode-' + Date.now() + '.json';
          document.body.appendChild(a); a.click(); a.remove();
          say('Episode handed to the browser as a download. If nothing was saved, this page is not allowed to download; run the lab from the planner service to save episodes to disk.', '');
        } catch (e) { say('This page cannot save files. Run the lab from the planner service to save episodes to disk.', ''); }
      };
      fetch(plannerBase() + '/v1/episodes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: text })
        .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
        .then(function (saved) { say('Episode saved' + (saved && saved.file ? ' as ' + esc(saved.file) : '') + '.', ''); }, fallback);
    }

    // ---- events -----------------------------------------------------------------------------------

    root.addEventListener('click', function (ev) {
      var t = ev.target.closest('button, [data-select]');
      if (!t || !root.contains(t)) return;
      if (t.dataset.stop) { lab.stop(t.dataset.stop); renderList(); renderChips(); ev.stopPropagation(); }
      else if (t.dataset.select) select(t.dataset.select);
      else if (t.dataset.space) { lab.setSpace(t.dataset.space, t.dataset.space === lab.space().name ? lab.space().seed : undefined); afterSpaceChange(); }
      else if (t.dataset.role === 'shuffle') { lab.setSpace(lab.space().name); afterSpaceChange(); }
      else if (t.dataset.camera) {
        lab.setCamera(t.dataset.camera);
        Array.prototype.forEach.call(root.querySelectorAll('[data-camera]'), function (b) { b.setAttribute('aria-pressed', String(b === t)); });
      }
      else if (t.dataset.builder) { builder = t.dataset.builder; lab.setFinder(builder === 'planner' ? plannerFinder : null); renderBuilder(); }
      else if (t.dataset.prompt) submit(t.dataset.prompt);
      else if (t.dataset.say) { lab.say(t.dataset.say); renderChips(); }
      else if (t.dataset.role === 'run') {
        var id = codeBox.dataset.id || 'custom', title = codeBox.dataset.title || 'Custom program';
        var shot = lab.lastCapture() || lab.capture();
        start({ id: id, title: title, prompt: codeBox.dataset.prompt || 'Edited in the lab', code: codeBox.value }, shot.view).then(function (settled) {
          if (!settled.error) say('Running your version of <b>' + esc(title) + '</b>.', title);
        });
      }
      else if (t.dataset.role === 'episode') saveEpisode();
    });

    function afterSpaceChange() {
      selected = null; lastEpisode = null; lastError = null;
      $('log').hidden = true;
      codeBox.value = ''; delete codeBox.dataset.id; delete codeBox.dataset.title; delete codeBox.dataset.prompt;
      $('code-title').textContent = 'Program';
      say('<span class="sl-note">' + (lab.space().props > 0 ? 'The stand-in pump is here. Pick a request or type your own.' : 'An unprepared space. Look around, then try one of the examples, or switch to the planner.') + '</span>', '');
      renderSpaces(); renderBuilder(); renderList(); renderChips();
    }

    seedBox.addEventListener('change', function () {
      var n = parseInt(seedBox.value, 10);
      if (!isNaN(n)) { lab.setSpace(lab.space().name, n); afterSpaceChange(); }
    });
    $('form').addEventListener('submit', function (ev) { ev.preventDefault(); submit(promptInput.value); });

    // The walk pad: hold to keep moving. Useful on phones, where there are no keys.
    var held = null, heldTimer = 0;
    function moveStep() {
      if (!held) return;
      if (held === 'f') lab.walk(0.05, 0); else if (held === 'b') lab.walk(-0.05, 0);
      else if (held === 'l') lab.walk(0, -0.05); else if (held === 'r') lab.walk(0, 0.05);
      else if (held === 'tl') lab.look(-2.2, 0); else if (held === 'tr') lab.look(2.2, 0);
    }
    root.querySelector('.sl-pad').addEventListener('pointerdown', function (ev) {
      var b = ev.target.closest('[data-move]');
      if (!b) return;
      ev.preventDefault();
      held = b.dataset.move;
      moveStep();
      clearInterval(heldTimer);
      heldTimer = setInterval(moveStep, 33);
    });
    var release = function () { held = null; clearInterval(heldTimer); };
    window.addEventListener('pointerup', release);
    window.addEventListener('pointercancel', release);

    lab.onEvent(function (ev) {
      if (ev.type === 'surface') {
        foundCount = ev.count;
        $('found').textContent = ev.count + ' surface' + (ev.count === 1 ? '' : 's') + ' found (latest: ' + ev.surface.kind + ')';
      } else if (ev.type === 'stopped' || ev.type === 'failed' || ev.type === 'started') {
        renderList(); renderChips();
      } else if (ev.type === 'log' && ev.id === selected) renderList();
    });

    var themeObserver = new MutationObserver(function () { lab.setTheme(currentTheme()); });
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    var mq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    var onScheme = function () { lab.setTheme(currentTheme()); };
    if (mq) { if (mq.addEventListener) mq.addEventListener('change', onScheme); else if (mq.addListener) mq.addListener(onScheme); }

    afterSpaceChange();

    return {
      lab: lab,
      submit: submit,
      setBuilder: function (name) { builder = name === 'planner' ? 'planner' : 'recorded'; lab.setFinder(builder === 'planner' ? plannerFinder : null); renderBuilder(); },
      refresh: function () { renderSpaces(); renderBuilder(); renderList(); renderChips(); },
      dispose: function () {
        release(); themeObserver.disconnect();
        window.removeEventListener('pointerup', release); window.removeEventListener('pointercancel', release);
        lab.dispose(); root.innerHTML = '';
      }
    };
  }

  // Loads the runtime library as text. It must not be loaded with a script tag: it is evaluated once per
  // experience, with that experience's own host.
  function loadLibrary(url) {
    if (window.SPATAIL_LIBRARY) return Promise.resolve(window.SPATAIL_LIBRARY);
    return fetch(url || 'data/spatail.js').then(function (r) {
      if (!r.ok) throw new Error('Could not load the Spatail library (' + r.status + ').');
      return r.text();
    });
  }

  window.SpatailLabUI = { mount: mount, loadLibrary: loadLibrary };
})();
