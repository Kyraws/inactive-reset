// Native input stays in the desktop host; the browser UI only edits bindings.
(() => {
  const panel = document.getElementById('inputBindingsPanel');
  const dialog = document.getElementById('bindingDialog');
  const prompt = document.getElementById('bindingPrompt');
  const clear = dialog.querySelector('[data-input=clear]');
  const bridge = window.chrome?.webview;
  const names = {capture:'Quick capture',teleport:'Teleport with speed & gear',standing:'Standing teleport',next:'Next Saved Checkpoint',previous:'Previous Saved Checkpoint',nextQuick:'Next Quick Capture',previousQuick:'Previous Quick Capture',clearQuick:'Clear Quick Captures'};
  let current, assignment;
  function send(command, action, extra = {}) { bridge?.postMessage({type:'inputBindings',command,action,...extra}); }
  function keyName(code) {
    if (code >= 112 && code <= 135) return 'F' + (code - 111);
    if (code >= 96 && code <= 105) return 'Num ' + (code - 96);
    return ({8:'Backspace',9:'Tab',13:'Enter',32:'Space',33:'Page Up',34:'Page Down',35:'End',36:'Home',37:'Left',38:'Up',39:'Right',40:'Down',45:'Insert',46:'Delete',106:'Num *',107:'Num +',109:'Num -',110:'Num .',111:'Num /',186:';',187:'=',188:',',189:'-',190:'.',191:'/',192:'`',219:'[',220:'\\',221:']',222:"'"})[code] || String.fromCharCode(code);
  }
  function keyboardLabel(key) { return key ? ((key.modifiers & 2 ? 'Ctrl+' : '') + (key.modifiers & 1 ? 'Alt+' : '') + (key.modifiers & 4 ? 'Shift+' : '') + keyName(key.keyCode)) : 'Unassigned'; }
  function bindingButton(action, kind, value) {
    const keyboard = kind === 'Keyboard';
    return `<button class="button quiet binding-trigger" data-input="learn${kind}" data-action="${action}" aria-label="${keyboard?'Keyboard shortcut':'Wheel button'} for ${esc(names[action])}: ${esc(value)}"><span class="binding-device" aria-hidden="true">${kind}: </span>${esc(value)}</button>`;
  }
  function restoreBindingFocus(action, kind) {
    panel.querySelector(`[data-action="${action}"][data-input="learn${kind}"]`)?.focus({preventScroll:true});
  }
  function closeAssignment(command) {
    if (!assignment) return;
    const {action, kind} = assignment;
    assignment = null;
    dialog.close();
    restoreBindingFocus(action, kind);
    if (command) send(command, action);
  }
  function render(detail) {
    current = detail;
    const focus = panel.contains(document.activeElement) ? document.activeElement.dataset : null;
    globalThis.practiceInputLabels = {};
    const rows = Object.entries(names).map(([action,name]) => {
      const b = detail.bindings[action] || {};
      const device = detail.devices.find(d => d.path.toLowerCase() === b.wheel?.devicePath.toLowerCase());
      const wheel = b.wheel ? (device?.name || 'Disconnected device') + ' · Button ' + b.wheel.button : 'Unassigned';
      globalThis.practiceInputLabels[action] = b.keyboard ? keyboardLabel(b.keyboard) : b.wheel ? 'Button ' + b.wheel.button : 'Unassigned';
      return `<div class="input-binding-row"><strong>${esc(name)}</strong>${bindingButton(action,'Keyboard',keyboardLabel(b.keyboard))}${bindingButton(action,'Wheel',wheel)}</div>`;
    }).join('');
    panel.innerHTML = '<h2>Keyboard & wheel buttons</h2><p class="field-help">Shortcuts run while LMU is focused.</p>' +
      '<p class="input-assignment-status" role="status">' + esc(detail.error || detail.message || '') + '</p>' +
      '<div class="binding-columns" aria-hidden="true"><span>Action</span><span>Keyboard</span><span>Wheel</span></div>' + rows +
      '<div class="dialog-actions"><button class="button" data-input="defaults">Restore default bindings</button></div><p class="small-note">Connected button devices: ' + esc(detail.devices.map(d => d.name).join(', ') || 'None detected') + '.</p>';
    document.getElementById('standingBtn').title = globalThis.practiceInputLabels.standing;
    if (assignment && detail.learningAction === assignment.action && detail.learningKind === assignment.kind.toLowerCase())
      assignment.started = true;
    else if (assignment && (assignment.started || detail.error))
      closeAssignment();
    else if (focus?.action)
      restoreBindingFocus(focus.action, focus.input.slice(5));
    else if (focus?.input === 'defaults')
      panel.querySelector('[data-input=defaults]').focus({preventScroll:true});
  }
  if (!bridge) { panel.innerHTML = '<h2>Keyboard & wheel buttons</h2><p>Open the Inactive Reset desktop app to assign keyboard shortcuts and wheel buttons.</p>'; return; }
  window.addEventListener('input-bindings', e => render(e.detail));
  panel.addEventListener('click', e => {
    const button = e.target.closest('[data-input]');
    if (!button) return;
    const {input, action} = button.dataset;
    if (input === 'defaults') { send(input); return; }
    const kind = input.slice(5);
    assignment = {action, kind, started:false};
    document.getElementById('bindingDialogTitle').textContent = names[action];
    prompt.textContent = kind === 'Keyboard'
      ? 'Press a shortcut. Use a modifier with a key, or F1–F24.'
      : 'Release, then press the wheel button. Only buttons can be assigned.';
    clear.hidden = !current.bindings[action]?.[kind.toLowerCase()];
    dialog.showModal();
    prompt.focus();
    send(input, action);
  });
  dialog.addEventListener('cancel', e => { e.preventDefault(); closeAssignment('cancel'); });
  dialog.addEventListener('click', e => {
    const input = e.target.closest('[data-input]')?.dataset.input;
    if (input && assignment) closeAssignment(input === 'clear' ? 'clear' + assignment.kind : 'cancel');
  });
  document.addEventListener('keydown', e => {
    if (!assignment) return;
    if (e.key === 'Escape') { e.preventDefault(); e.stopImmediatePropagation(); closeAssignment('cancel'); return; }
    if (!assignment.started || assignment.kind !== 'Keyboard') return;
    const modified = e.ctrlKey || e.altKey || e.shiftKey;
    if ((!e.ctrlKey && !e.altKey && e.key === 'Tab') ||
        (!modified && e.target instanceof HTMLButtonElement && ['Enter',' '].includes(e.key))) return;
    e.preventDefault(); e.stopImmediatePropagation();
    if (e.repeat || ['Control','Alt','Shift','Meta'].includes(e.key)) return;
    send('keyboard',assignment.action,{keyCode:e.keyCode,modifiers:(e.altKey?1:0)|(e.ctrlKey?2:0)|(e.shiftKey?4:0)});
  }, true);
  send('get');
})();
