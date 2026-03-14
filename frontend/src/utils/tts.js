// Text-to-Speech utility for ETDP App

let activeAudio = null;
let activeAudioUrl = '';

const clampNumber = (value, min, max, fallback) => {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
};

const readErrorMessage = async (res) => {
  const raw = await res.text();
  if (!raw) return `HTTP ${res.status}`;
  try {
    const parsed = JSON.parse(raw);
    if (typeof parsed === 'string') return parsed;
    return String(parsed?.error || parsed?.message || parsed?.title || raw);
  } catch {
    return raw;
  }
};

const resetActiveAudio = () => {
  if (activeAudio) {
    try {
      activeAudio.pause();
      activeAudio.src = '';
    } catch {
      // ignore
    }
  }
  activeAudio = null;
  if (activeAudioUrl && typeof window !== 'undefined') {
    try {
      window.URL.revokeObjectURL(activeAudioUrl);
    } catch {
      // ignore
    }
  }
  activeAudioUrl = '';
};

export function stopSpeech() {
  if (typeof window !== 'undefined' && window.speechSynthesis) {
    try {
      window.speechSynthesis.cancel();
    } catch {
      // ignore
    }
  }
  resetActiveAudio();
}

const waitForVoices = (synth, timeoutMs = 1500) => new Promise((resolve) => {
  const immediate = synth.getVoices?.() || [];
  if (immediate.length > 0) {
    resolve(immediate);
    return;
  }

  let done = false;
  const finish = (voices) => {
    if (done) return;
    done = true;
    try {
      if (typeof synth.removeEventListener === 'function') {
        synth.removeEventListener('voiceschanged', onVoicesChanged);
      } else {
        synth.onvoiceschanged = null;
      }
    } catch {
      // ignore
    }
    resolve(Array.isArray(voices) ? voices : (synth.getVoices?.() || []));
  };

  const onVoicesChanged = () => finish(synth.getVoices?.() || []);

  try {
    if (typeof synth.addEventListener === 'function') {
      synth.addEventListener('voiceschanged', onVoicesChanged);
    } else {
      synth.onvoiceschanged = onVoicesChanged;
    }
  } catch {
    // ignore
  }

  window.setTimeout(() => finish(synth.getVoices?.() || []), Math.max(200, Number(timeoutMs) || 1500));
});

const pickVoice = (voices, lang, preferredName) => {
  if (!Array.isArray(voices) || voices.length === 0) return null;
  const requestedLang = String(lang || 'en-US').toLowerCase();
  const requestedBaseLang = requestedLang.split('-')[0];
  const preferred = String(preferredName || '').toLowerCase();

  if (preferred) {
    const byName = voices.find((v) =>
      String(v?.name || '').toLowerCase().includes(preferred) &&
      String(v?.lang || '').toLowerCase().startsWith(requestedBaseLang));
    if (byName) return byName;
  }

  const exactLang = voices.find((v) => String(v?.lang || '').toLowerCase() === requestedLang);
  if (exactLang) return exactLang;

  const sameBaseLang = voices.find((v) => String(v?.lang || '').toLowerCase().startsWith(requestedBaseLang));
  if (sameBaseLang) return sameBaseLang;

  const englishFallback = voices.find((v) => String(v?.lang || '').toLowerCase().startsWith('en'));
  if (englishFallback) return englishFallback;

  return voices[0] || null;
};

const speakBrowser = async (text, options = {}) => {
  if (typeof window === 'undefined') {
    return { ok: false, reason: 'window_unavailable' };
  }

  const synth = window.speechSynthesis;
  if (!synth || typeof window.SpeechSynthesisUtterance === 'undefined') {
    return { ok: false, reason: 'speech_not_supported' };
  }

  const content = String(text || '').trim();
  if (!content) {
    return { ok: false, reason: 'empty_text' };
  }

  stopSpeech();

  const lang = String(options.lang || 'en-US');
  const utterance = new window.SpeechSynthesisUtterance(content);
  utterance.lang = lang;
  utterance.rate = clampNumber(options.rate, 0.6, 2.0, 1.0);
  utterance.pitch = clampNumber(options.pitch, 0.5, 2.0, 1.0);
  utterance.volume = clampNumber(options.volume, 0.0, 1.0, 1.0);

  const voices = await waitForVoices(synth, 1800);
  const voice = pickVoice(voices, lang, options.preferredVoiceName || 'Microsoft');
  if (voice) utterance.voice = voice;

  return new Promise((resolve) => {
    let settled = false;
    let started = false;

    const finish = (result) => {
      if (settled) return;
      settled = true;
      resolve(result);
    };

    utterance.onstart = () => {
      started = true;
      finish({
        ok: true,
        started: true,
        provider: 'browser',
        voiceName: String(utterance.voice?.name || ''),
        lang: String(utterance.voice?.lang || utterance.lang || '')
      });
    };

    utterance.onerror = (event) => {
      const reason = String(event?.error || 'tts_error');
      const message = String(event?.message || '');
      finish({ ok: false, reason, message, provider: 'browser' });
    };

    utterance.onend = () => {
      if (!started) {
        finish({ ok: false, reason: 'ended_without_start', provider: 'browser' });
      }
    };

    try {
      synth.speak(utterance);
      if (synth.paused) synth.resume();
    } catch (error) {
      finish({ ok: false, reason: 'speak_exception', message: String(error?.message || error), provider: 'browser' });
      return;
    }

    window.setTimeout(() => {
      if (!settled && !synth.speaking && !synth.pending) {
        finish({ ok: false, reason: 'not_started', provider: 'browser' });
      }
    }, 900);
  });
};

const speakOpenAi = async (text, options = {}) => {
  if (typeof window === 'undefined') {
    return { ok: false, reason: 'window_unavailable' };
  }

  const content = String(text || '').trim();
  if (!content) {
    return { ok: false, reason: 'empty_text' };
  }

  stopSpeech();

  const payload = {
    provider: 'openai',
    text: content,
    model: String(options.model || '').trim() || null,
    voice: String(options.voice || '').trim() || null,
    format: String(options.format || 'mp3').trim().toLowerCase(),
    speed: clampNumber(options.speed, 0.25, 4.0, 1.0)
  };

  const res = await fetch('/api/TextToVideo/tts-preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  if (!res.ok) {
    const message = await readErrorMessage(res);
    return { ok: false, reason: 'openai_http_error', message };
  }

  const blob = await res.blob();
  if (!blob || blob.size <= 0) {
    return { ok: false, reason: 'empty_audio' };
  }

  activeAudioUrl = window.URL.createObjectURL(blob);
  const audio = new Audio(activeAudioUrl);
  activeAudio = audio;
  audio.preload = 'auto';

  return new Promise((resolve) => {
    let settled = false;

    const finish = (result) => {
      if (settled) return;
      settled = true;
      resolve(result);
    };

    audio.addEventListener('playing', () => {
      finish({
        ok: true,
        started: true,
        provider: 'openai',
        model: String(payload.model || ''),
        voiceName: String(payload.voice || ''),
        format: String(payload.format || 'mp3')
      });
    }, { once: true });

    audio.addEventListener('error', () => {
      finish({ ok: false, reason: 'audio_playback_error', provider: 'openai' });
    }, { once: true });

    audio.play()
      .catch((error) => {
        finish({ ok: false, reason: 'audio_play_exception', message: String(error?.message || error), provider: 'openai' });
      });

    window.setTimeout(() => {
      if (!settled) {
        finish({ ok: false, reason: 'audio_not_started', provider: 'openai' });
      }
    }, 1500);
  });
};

export async function speak(text, options = {}) {
  const provider = String(options.provider || 'browser').trim().toLowerCase();
  if (provider === 'openai') {
    return speakOpenAi(text, options);
  }
  return speakBrowser(text, options);
}
