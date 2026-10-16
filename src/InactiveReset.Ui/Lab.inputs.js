// Native input stays in the desktop host; the browser UI only edits bindings.
(() => {
  const panel = document.getElementById('inputBindingsPanel');
  const bridge = window.chrome?.webview;
  const names = {capture:'Quick capture',teleport:'Teleport with speed & gear',standing:'Standing teleport',previous:'Previous checkpoint',next:'Next checkpoint'};
  let current;
  function send(command, action, extra = {}) { bridge?.postMessage({type:'inputBindings',command,action,...extra}); }
  function keyName(code) {
    if (code >= 112 && code <= 135) return 'F' + (code - 111);
    if (code >= 96 && code <= 105) return 'Num ' + (code - 96);
    return ({8:'Backspace',9:'Tab',13:'Enter',32:'Space',33:'Page Up',34:'Page Down',35:'End',36:'Home',37:'Left',38:'Up',39:'Right',40:'Down',45:'Insert',46:'Delete',106:'Num *',107:'Num +',109:'Num -',110:'Num .',111:'Num /',186:';',187:'=',188:',',189:'-',190:'.',191:'/',192:'`',219:'[',220:'\\',221:']',222:"'"})[code] || String.fromCharCode(code);
  }
  function keyboardLabel(key) { return key ? ((key.modifiers & 2 ? 'Ctrl+' : '') + (key.modifiers & 1 ? 'Alt+' : '') + (key.modifiers & 4 ? 'Shift+' : '') + keyName(key.keyCode)) : 'Unassigned'; }
  function render(detail) {
    current = detail;
    globalThis.practiceInputLabels = {};
    const rows = Object.entries(names).map(([action,name]) => {
      const b = detail.bindings[action];
      const device = detail.devices.find(d => d.path.toLowerCase() === b.wheel?.devicePath.toLowerCase());
      const wheel = b.wheel ? (device?.name || 'Disconnected device') + ' · Button ' + b.wheel.button : 'Unassigned';
      globalThis.practiceInputLabels[action] = b.keyboard ? keyboardLabel(b.keyboard) : b.wheel ? 'Button ' + b.wheel.button : 'Unassigned';
      return '<div class="input-binding-row"><strong>' + esc(name) + '</strong><div><span>' + esc(keyboardLabel(b.keyboard)) + '</span><button class="button quiet" data-input="learnKeyboard" data-action="' + action + '">Assign key</button><button class="text-button" data-input="clearKeyboard" data-action="' + action + '">Clear key</button></div><div><span>' + esc(wheel) + '</span><button class="button quiet" data-input="learnWheel" data-action="' + action + '">Assign button</button><button class="text-button" data-input="clearWheel" data-action="' + action + '">Clear button</button></div></div>';
    }).join('');
    const learning = detail.learningAction ? 'Assigning ' + names[detail.learningAction] + ': ' + (detail.learningKind === 'keyboard' ? 'press a shortcut (Escape cancels).' : 'release, then press the wheel button. If the first press is not detected, release and press again.') : '';
    panel.innerHTML = '<h2>Keyboard & wheel buttons</h2><p class="section-caption">Actions run with LMU focused. Previous/Next cycles speed-ready checkpoints for the current track and car in lap order, including Quick capture. Selection wraps; it does not teleport.</p>' + rows +
      '<p class="input-assignment-status" role="status">' + esc(detail.error || learning || detail.message || 'Bindings saved automatically. Use a modifier with a key, or F1–F24. Keyboard shortcuts are reserved while this app is open; avoid shortcuts you need in other apps.') + '</p>' +
      '<div class="dialog-actions"><button class="button" data-input="cancel"' + (detail.learningAction ? '' : ' hidden') + '>Cancel assignment</button><button class="button" data-input="defaults">Restore default bindings</button></div><p class="small-note">Connected button devices: ' + esc(detail.devices.map(d => d.name).join(', ') || 'None detected') + '. Axes and hat switches are not assignable. Wheel inputs remain available to LMU.</p>';
    document.getElementById('standingBtn').title = globalThis.practiceInputLabels.standing;
  }
  if (!bridge) { panel.innerHTML = '<h2>Keyboard & wheel buttons</h2><p>Open the Inactive Reset desktop app to assign keyboard shortcuts and wheel buttons.</p>'; return; }
  window.addEventListener('input-bindings', e => render(e.detail));
  panel.addEventListener('click', e => { const button = e.target.closest('[data-input]'); if (button) send(button.dataset.input,button.dataset.action); });
  document.addEventListener('keydown', e => {
    if (!current?.learningAction) return;
    if (e.key === 'Escape') { e.preventDefault(); e.stopImmediatePropagation(); send('cancel'); return; }
    if (current.learningKind !== 'keyboard') return;
    e.preventDefault(); e.stopImmediatePropagation();
    if (e.repeat || ['Control','Alt','Shift','Meta'].includes(e.key)) return;
    send('keyboard',current.learningAction,{keyCode:e.keyCode,modifiers:(e.altKey?1:0)|(e.ctrlKey?2:0)|(e.shiftKey?4:0)});
  }, true);
  send('get');
})();
