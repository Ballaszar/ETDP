import React, { useState, useRef, useEffect } from 'react';
import './AIAgentChat.css';

const AGENT_NAME = 'Mira Your Lecturer';
const MIRA_AVATAR_SRC = `${import.meta.env.BASE_URL}mira-face.png`;
const defaultWelcome = [
  { sender: 'ai', text: `Hi! I am ${AGENT_NAME}. How can I help you today?` }
];

const API_KNOWLEDGE = '/api/Knowledge/development-readme';

export default function AIAgentChat() {
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState(defaultWelcome);
  const [input, setInput] = useState('');
  const [typing, setTyping] = useState(false);
  const chatEndRef = useRef(null);
  const [kb, setKb] = useState({ name: '', content: '' });
  const [sections, setSections] = useState([]);

  useEffect(() => {
    if (open && chatEndRef.current) {
      chatEndRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages, open]);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const tid = setTimeout(() => controller.abort(), 3000);
    (async () => {
      try {
        const r = await fetch(API_KNOWLEDGE, { signal: controller.signal });
        if (!r.ok) return;
        const json = await r.json();
        setKb(json);
        const text = String(json.content || '');
        const lines = text.split(/\r?\n/);
        const out = [];
        let current = { title: 'Intro', body: [] };
        for (const line of lines) {
          const m = line.match(/^##+ (.+)$/);
          if (m) {
            if (current.body.length) out.push({ title: current.title, body: current.body.join('\n') });
            current = { title: m[1].trim(), body: [] };
          } else {
            current.body.push(line);
          }
        }
        if (current.body.length) out.push({ title: current.title, body: current.body.join('\n') });
        setSections(out);
      } catch {}
    })();
    return () => {
      clearTimeout(tid);
      controller.abort();
    };
  }, [open]);

  const sendMessage = (e) => {
    e.preventDefault();
    if (!input.trim()) return;
    setMessages(msgs => [...msgs, { sender: 'user', text: input }]);
    setTyping(true);
    setTimeout(() => {
      const q = input.toLowerCase();
      const words = q.split(/\W+/).filter(w => w.length > 2);
      let best = null;
      let bestScore = 0;
      for (const s of sections) {
        const text = `${String(s.title || '').toLowerCase()} ${String(s.body || '').toLowerCase()}`;
        let score = 0;
        for (const w of words) {
          if (text.includes(w)) score++;
        }
        if (score > bestScore) { bestScore = score; best = s; }
      }
      let response = '';
      if (best && best.body) {
        response = best.body.slice(0, 800);
      } else if (q.includes('builder') || q.includes('engine')) {
        const engine = sections.find(s => s.title.toLowerCase().includes('content builder'));
        response = engine?.body || 'Use Content Builder to search, upload materials, and insert selected text into Lesson Plan content.';
      } else if (q.includes('exports') || q.includes('download')) {
        const exp = sections.find(s => s.title.toLowerCase().includes('exports'));
        response = exp?.body || 'Use Dashboard export cards to generate documents once data is complete.';
      } else if (q.includes('qualification') || q.includes('subject') || q.includes('topic') || q.includes('criteria')) {
        response = 'Use Content Builder filters: Qualification → Subject → Topic → Assessment Criteria. Ensure qualificationId uses the internal Id.';
      } else {
        const titles = sections.map(s => s.title).slice(0, 5).join(', ');
        response = `I searched ${kb.name}. Try these sections: ${titles}.`;
      }
      setMessages(msgs => [...msgs, { sender: 'ai', text: response }]);
      setTyping(false);
    }, 500);
    setInput('');
  };

  useEffect(() => {
    if (!messages.length) return;
    const last = messages[messages.length - 1];
    if (last.sender !== 'user') return;
    const text = last.text || '';
    const run = async () => {
      try {
        const res = await fetch(`/api/Code/search?text=${encodeURIComponent(text)}&limit=5`);
        if (!res.ok) return;
        const hits = await res.json();
        if (Array.isArray(hits) && hits.length) {
          const summary = hits.map(h => {
            const p = String(h.path || '').split(/[\\/]/).slice(-3).join('/');
            const s = String(h.snippet || '').replace(/\s+/g, ' ').slice(0, 200);
            return `${p}: ${s}`;
          }).join('\n');
          setMessages(msgs => [...msgs, { sender: 'ai', text: `Code matches:\n${summary}` }]);
        }
      } catch {}
    };
    run();
  }, [messages]);

  return (
    <div className={`ai-agent-chat-root${open ? ' open' : ''}`}> 
      <button className="ai-agent-toggle" onClick={() => setOpen(o => !o)}>
        {open ? `Close ${AGENT_NAME}` : `Open ${AGENT_NAME}`}
      </button>
      {open && (
        <div className="ai-agent-chat-window">
          <div className="ai-agent-chat-header">
            <div className={`ai-agent-chat-avatar${typing ? ' talking' : ''}`} aria-hidden="true">
              <img className="ai-agent-chat-avatar-image" src={MIRA_AVATAR_SRC} alt="" loading="eager" decoding="async" />
            </div>
            <div className="ai-agent-chat-header-text">
              <strong>{AGENT_NAME}</strong>
              <span>{typing ? 'Replying...' : 'Workflow + content assistant'}</span>
            </div>
          </div>
          <div className="ai-agent-chat-messages">
            {messages.map((m, i) => (
              <div key={i} className={`ai-msg ai-msg-${m.sender}`}>{m.text}</div>
            ))}
            {typing ? (
              <div className="ai-msg ai-msg-ai ai-msg-typing" aria-label="Mira is typing">
                <span />
                <span />
                <span />
              </div>
            ) : null}
            <div ref={chatEndRef} />
          </div>
          <form className="ai-agent-chat-input" onSubmit={sendMessage}>
            <input
              value={input}
              onChange={e => setInput(e.target.value)}
              placeholder="Type your question..."
              autoFocus
            />
            <button type="submit">Send</button>
          </form>
        </div>
      )}
    </div>
  );
}
