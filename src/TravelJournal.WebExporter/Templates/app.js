const state = {
  manifest:     null,
  index:        0,
  paused:       false,
  itemTimer:    null,
  overlayTimer: null,
  cursorTimer:  null,
};

let activeEl  = null;
let pendingEl = null;

// ── Bootstrap ─────────────────────────────────────────────

async function init() {
  activeEl  = document.getElementById('slide-a');
  pendingEl = document.getElementById('slide-b');

  state.manifest = window.__TOUR__;

  if (!state.manifest?.items?.length) {
    showError('Keine Inhalte gefunden.');
    return;
  }

  document.title = state.manifest.title ?? document.title;

  setupKeyboard();
  setupCursorHide();

  preload(state.manifest.items[0]?.src);
  show(0);
  scheduleHintFade();
}

// ── Navigation ─────────────────────────────────────────────

function show(index) {
  clearTimeout(state.itemTimer);

  if (index < 0) index = 0;
  if (index >= state.manifest.items.length) {
    finish();
    return;
  }

  state.index = index;
  const item = state.manifest.items[index];

  crossfadeTo(item.src);
  updateOverlay(item);
  preload(state.manifest.items[index + 1]?.src);

  if (!state.paused) {
    const dur = state.manifest.photoDurationMs ?? 5000;
    state.itemTimer = setTimeout(() => show(state.index + 1), dur);
  }
}

function togglePause() {
  state.paused = !state.paused;
  if (state.paused) {
    clearTimeout(state.itemTimer);
  } else {
    const dur = state.manifest.photoDurationMs ?? 5000;
    state.itemTimer = setTimeout(() => show(state.index + 1), dur);
  }
}

function finish() {
  clearTimeout(state.itemTimer);
  clearTimeout(state.overlayTimer);
  state.paused = true;
  document.getElementById('overlay').classList.remove('visible');
  document.getElementById('finish').hidden = false;
}

// ── Image crossfade ───────────────────────────────────────

function crossfadeTo(src) {
  pendingEl.src = src;

  const swap = () => {
    pendingEl.style.zIndex = '2';
    pendingEl.style.opacity = '1';
    activeEl.style.opacity = '0';

    setTimeout(() => {
      activeEl.style.zIndex = '0';
      // Swap roles
      const tmp = activeEl;
      activeEl  = pendingEl;
      pendingEl = tmp;
    }, 320);
  };

  if (pendingEl.complete && pendingEl.naturalWidth > 0) {
    swap();
  } else {
    pendingEl.onload = swap;
    pendingEl.onerror = swap; // show even on error
  }
}

function preload(src) {
  if (!src) return;
  const img = new Image();
  img.src = src;
}

// ── Overlay ───────────────────────────────────────────────

function updateOverlay(item) {
  clearTimeout(state.overlayTimer);
  const overlay = document.getElementById('overlay');
  const locEl   = document.getElementById('overlay-location');
  const dtEl    = document.getElementById('overlay-datetime');

  const hasTitle    = !!item.title?.trim();
  const hasLocation = !!item.location?.trim();
  const hasDateTime = !!item.dateTime;

  if (!hasTitle && !hasLocation && !hasDateTime) {
    overlay.classList.remove('visible');
    return;
  }

  if (hasTitle) {
    locEl.textContent = item.title;
    dtEl.textContent  = '';
  } else {
    locEl.textContent = item.location ?? '';
    dtEl.textContent  = hasDateTime
      ? new Intl.DateTimeFormat('de-DE', {
          weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
          hour: '2-digit', minute: '2-digit'
        }).format(new Date(item.dateTime)).replace(',', ' ·')
      : '';
  }

  overlay.classList.add('visible');
  const visMs = state.manifest.overlayVisibleMs ?? 2000;
  state.overlayTimer = setTimeout(() => overlay.classList.remove('visible'), visMs);
}

// ── Keyboard ──────────────────────────────────────────────

function setupKeyboard() {
  document.addEventListener('keydown', e => {
    switch (e.key) {
      case 'Escape':
        finish();
        break;
      case ' ':
        e.preventDefault();
        togglePause();
        break;
      case 'ArrowRight':
        show(state.index + 1);
        break;
      case 'ArrowLeft':
        show(Math.max(0, state.index - 1));
        break;
      case 'f':
      case 'F':
        if (!document.fullscreenElement)
          document.documentElement.requestFullscreen().catch(() => {});
        else
          document.exitFullscreen().catch(() => {});
        break;
    }
  });
}

// ── Cursor hide ───────────────────────────────────────────

function setupCursorHide() {
  const show = () => {
    document.body.classList.remove('cursor-hidden');
    clearTimeout(state.cursorTimer);
    state.cursorTimer = setTimeout(
      () => document.body.classList.add('cursor-hidden'),
      2000
    );
  };
  document.addEventListener('mousemove', show);
  show();
}

// ── Hint fade ─────────────────────────────────────────────

function scheduleHintFade() {
  setTimeout(() => {
    const hint = document.getElementById('hint');
    if (hint) hint.classList.add('hidden');
  }, 4000);
}

// ── Error ─────────────────────────────────────────────────

function showError(msg) {
  document.body.style.cssText = 'background:#000;color:#f55;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif;font-size:24px';
  document.body.textContent = msg;
}

init();
