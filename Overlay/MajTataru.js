'use strict';

// ─── OverlayPlugin API Bootstrap ───
// Equivalent to common.min.js — defines addOverlayListener / callOverlayHandler
// without requiring an external CDN fetch.
(function() {
  if (window.addOverlayListener) return;

  var subscribers = {};
  var sendQueue = [];
  var rseqCounter = 0;
  var responsePromises = {};
  var ready = false;
  var wsUrl = /[?&]OVERLAY_WS=([^&]+)/.exec(location.href);

  function processEvent(msg) {
    if (msg.rseq !== undefined && responsePromises[msg.rseq]) {
      responsePromises[msg.rseq](msg);
      delete responsePromises[msg.rseq];
      return;
    }
    if (msg.type && subscribers[msg.type]) {
      for (var i = 0; i < subscribers[msg.type].length; i++) {
        subscribers[msg.type][i](msg);
      }
    }
  }

  function sendMessage(obj, cb) {
    if (cb) {
      var rseq = rseqCounter++;
      obj.rseq = rseq;
      responsePromises[rseq] = cb;
    }
    if (!ready) {
      sendQueue.push(obj);
    } else {
      doSend(obj);
    }
  }

  var doSend;

  window.addOverlayListener = function(event, cb) {
    if (!subscribers[event]) {
      subscribers[event] = [];
      if (ready) sendMessage({ call: 'subscribe', events: [event] });
    }
    subscribers[event].push(cb);
  };

  window.removeOverlayListener = function(event, cb) {
    if (subscribers[event]) {
      var idx = subscribers[event].indexOf(cb);
      if (idx > -1) subscribers[event].splice(idx, 1);
    }
  };

  window.callOverlayHandler = function(msg) {
    return new Promise(function(resolve) {
      sendMessage(msg, function(data) {
        resolve(data === undefined ? null : data);
      });
    });
  };

  function flush() {
    ready = true;
    var events = Object.keys(subscribers);
    if (events.length > 0) sendMessage({ call: 'subscribe', events: events });
    for (var i = 0; i < sendQueue.length; i++) doSend(sendQueue[i]);
    sendQueue = [];
  }

  if (wsUrl) {
    var ws = new WebSocket(decodeURIComponent(wsUrl[1]));
    doSend = function(obj) { ws.send(JSON.stringify(obj)); };
    ws.onmessage = function(e) { processEvent(JSON.parse(e.data)); };
    ws.onopen = flush;
  } else {
    doSend = function(obj) {
      window.OverlayPluginApi.callHandler(JSON.stringify(obj), function(r) {
        if (r) processEvent(JSON.parse(r));
      });
    };
    window.__OverlayCallback = processEvent;

    var waitApi = function() {
      if (window.OverlayPluginApi && window.OverlayPluginApi.ready) {
        flush();
      } else {
        setTimeout(waitApi, 100);
      }
    };
    waitApi();
  }
})();

// ─── Configuration ───
var DISPLAY_DURATION = 15000;
var FADE_DURATION = 600;
var MAX_DETAIL_ROWS = 4;
var TTS_ENABLED = true;
var TTS_LANG = 'zh-CN';
var TTS_RATE = 1.3;
var TTS_VOLUME = 1.0;

// ─── State ───
var fadeTimer = null;
var ttsUtterances = [];

// ─── DOM Refs ───
var elStatus;
var elMain;
var elDetails;

document.addEventListener('DOMContentLoaded', function() {
  elStatus = document.getElementById('status-bar');
  elMain = document.getElementById('main-rec');
  elDetails = document.getElementById('details');

  addOverlayListener('LogLine', onLogLine);
  startOverlay();
});

function startOverlay() {
  if (typeof callOverlayHandler === 'function') {
    callOverlayHandler({ call: 'subscribe', events: ['LogLine'] });
  }
}

// ─── LogLine Handler ───
function onLogLine(e) {
  var detail = e.detail || e;
  var line = detail.line;
  if (!line || line[0] !== '00') return;
  if (line[3] !== 'MajTataru') return;

  var raw = line[4];
  if (!raw) return;

  try {
    raw = raw.replace(/\u2502/g, '|');
    var data = JSON.parse(raw);
    handleData(data);
  } catch (ex) {
    // ignore malformed lines
  }
}

// ─── Data Dispatch ───
function handleData(data) {
  if (!data || !data.type) return;

  if (data.type === 'discard') {
    showDiscard(data);
  } else if (data.type === 'call') {
    showCall(data);
  }

  if (TTS_ENABLED && data.tts) {
    speak(data.tts);
  }

  scheduleHide();
}

// ─── Discard Display ───
function showDiscard(d) {
  // status bar
  var parts = [];
  if (d.strategy) parts.push(d.strategy);
  if (typeof d.shanten === 'number') parts.push('向听 ' + d.shanten);
  if (typeof d.tilesLeft === 'number') parts.push('剩余 ' + d.tilesLeft + '张');
  if (d.wind) parts.push('自风 ' + d.wind);

  elStatus.textContent = parts.join('  ·  ');
  elStatus.classList.remove('hidden');

  // main recommendation
  elMain.className = '';
  if (d.isFold) {
    elMain.textContent = '弃和模式 — 安全牌优先';
    elMain.classList.add('fold');
  } else if (d.riichi && d.bestTileName) {
    elMain.textContent = '切 ' + d.bestTileName + '  立直!';
    elMain.classList.add('riichi');
  } else if (d.bestTileName) {
    elMain.textContent = '切 ' + d.bestTileName;
    elMain.classList.add('discard');
  } else {
    elMain.textContent = '';
    elMain.classList.add('hidden');
    return;
  }
  elMain.classList.remove('hidden');
  replayAnimation(elMain);

  // detail rows
  elDetails.innerHTML = '';
  var recs = d.recommendations || [];
  var count = Math.min(recs.length, MAX_DETAIL_ROWS);
  for (var i = 0; i < count; i++) {
    var r = recs[i];
    var row = document.createElement('div');
    row.className = 'row rank-' + Math.min(r.rank || (i + 1), 3);
    if (r.danger > 50) row.className += ' danger-high';

    var text = '#' + (r.rank || (i + 1)) + ' ' + (r.tile || '?');
    text += '  效率=' + fmtNum(r.efficiency, 2);
    text += '  危险=' + fmtNum(r.danger, 1);
    if (typeof r.shanten === 'number') text += '  向听=' + r.shanten;
    if (r.safe === false) text += ' [危]';
    row.textContent = text;
    elDetails.appendChild(row);
  }

  if (d.tenpaiInfo) {
    var ti = document.createElement('div');
    ti.className = 'row rank-1';
    var tText = '听牌: 待牌=' + fmtNum(d.tenpaiInfo.waits, 1);
    tText += '  形状=' + fmtNum(d.tenpaiInfo.shape, 2);
    tText += d.tenpaiInfo.riichi ? '  → 建议立直' : '  → 不建议立直';
    ti.textContent = tText;
    elDetails.appendChild(ti);
  }

  elDetails.classList.remove('hidden');
}

// ─── Call Display ───
function showCall(d) {
  var parts = [];
  if (d.player) parts.push(d.player);
  if (d.discardedTile) parts.push('打出 ' + d.discardedTile);

  elStatus.textContent = '▶ ' + parts.join(' ');
  elStatus.classList.remove('hidden');

  var advices = d.advices || [];
  var hasRec = advices.some(function(a) { return a.recommended; });

  elMain.className = '';
  if (hasRec) {
    var best = advices.filter(function(a) { return a.recommended; })[0];
    var mainText = '★ ' + best.callType;
    if (best.tiles) mainText += ' [' + best.tiles + ']';
    if (best.discard) mainText += '  →  打 ' + best.discard;
    elMain.textContent = mainText;
    elMain.classList.add('call-yes');
  } else {
    elMain.textContent = '不鸣牌 — 跳过';
    elMain.classList.add('call-no');
  }
  elMain.classList.remove('hidden');
  replayAnimation(elMain);

  elDetails.innerHTML = '';
  for (var i = 0; i < advices.length; i++) {
    var a = advices[i];
    var row = document.createElement('div');
    row.className = 'row ' + (a.recommended ? 'rec-yes' : 'rec-no');

    var text = a.callType;
    if (a.tiles) text += ' [' + a.tiles + ']';
    if (a.discard) text += ' → 打' + a.discard;
    text += ': ' + (a.recommended ? '★推荐' : '不推荐');
    if (a.reason) text += ' | ' + a.reason;
    row.textContent = text;
    elDetails.appendChild(row);

    if (typeof a.shantenBefore === 'number') {
      var sub = document.createElement('div');
      sub.className = 'row ' + (a.recommended ? 'rec-yes' : 'rec-no');
      sub.textContent = '    向听: ' + a.shantenBefore + '→' + a.shantenAfter +
        '  预估得分: ' + fmtNum(a.score, 0);
      elDetails.appendChild(sub);
    }
  }
  elDetails.classList.remove('hidden');
}

// ─── TTS ───
function speak(text) {
  if (!window.speechSynthesis) return;

  window.speechSynthesis.cancel();

  var utter = new SpeechSynthesisUtterance(text);
  utter.lang = TTS_LANG;
  utter.rate = TTS_RATE;
  utter.volume = TTS_VOLUME;

  var voices = window.speechSynthesis.getVoices();
  for (var i = 0; i < voices.length; i++) {
    if (voices[i].lang && voices[i].lang.indexOf('zh') === 0) {
      utter.voice = voices[i];
      break;
    }
  }

  window.speechSynthesis.speak(utter);
}

// preload voices
if (window.speechSynthesis) {
  window.speechSynthesis.getVoices();
  if (window.speechSynthesis.onvoiceschanged !== undefined) {
    window.speechSynthesis.onvoiceschanged = function() {};
  }
}

// ─── Auto-hide ───
function scheduleHide() {
  if (fadeTimer) clearTimeout(fadeTimer);

  elStatus.classList.remove('fade-out');
  elMain.classList.remove('fade-out');
  elDetails.classList.remove('fade-out');

  fadeTimer = setTimeout(function() {
    elStatus.classList.add('fade-out');
    elMain.classList.add('fade-out');
    elDetails.classList.add('fade-out');

    setTimeout(function() {
      elStatus.classList.add('hidden');
      elMain.classList.add('hidden');
      elDetails.classList.add('hidden');
      elStatus.classList.remove('fade-out');
      elMain.classList.remove('fade-out');
      elDetails.classList.remove('fade-out');
    }, FADE_DURATION);
  }, DISPLAY_DURATION);
}

// ─── Helpers ───
function fmtNum(val, decimals) {
  if (typeof val !== 'number') return '-';
  return val.toFixed(decimals);
}

function replayAnimation(el) {
  el.style.animation = 'none';
  void el.offsetWidth;
  el.style.animation = '';
}
