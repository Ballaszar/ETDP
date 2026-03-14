import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import axios from 'axios';
import App from './App';
import './index.css';
import { getClientCorrelationId, reportClientError } from './utils/diagnostics';

import { QualificationProvider } from './context/QualificationContext';
import { GlossaryProvider } from './context/GlossaryContext';

const originalFetch = window.fetch.bind(window);
const configuredApiBase = String(
    import.meta.env.VITE_API_BASE ||
    globalThis.__ETDP_API_BASE__ ||
    '/api'
).trim().replace(/\/+$/, '');
const apiDisabled = (() => {
    const raw = String(import.meta.env.VITE_DISABLE_API || globalThis.__ETDP_DISABLE_API__ || '').trim().toLowerCase();
    return raw === '1' || raw === 'true' || raw === 'yes' || raw === 'on';
})();
const configuredDevApiBase = String(import.meta.env.VITE_DEV_API_BASE || 'http://localhost:5299').trim().replace(/\/+$/, '');
const devApiFallbackBases = import.meta.env.DEV
    ? [configuredDevApiBase]
    : [];

const toUrlString = (input) => {
    if (!input) return '';
    if (typeof input === 'string') return input;
    if (input instanceof URL) return input.toString();
    if (input instanceof Request) return input.url;
    return String(input);
};

const isAbsoluteHttpUrl = (value) => {
    const s = String(value || '').trim().toLowerCase();
    return s.startsWith('http://') || s.startsWith('https://');
};

const isSameOriginApiRequest = (value) => {
    const s = toUrlString(value);
    if (!s) return false;
    try {
        const u = new URL(s, window.location.origin);
        return u.origin === window.location.origin && u.pathname.startsWith('/api');
    } catch {
        return s.startsWith('/api');
    }
};

const getApiPathFromUrl = (value) => {
    const s = toUrlString(value);
    if (!s) return '';
    try {
        const u = new URL(s, window.location.origin);
        if (!u.pathname.startsWith('/api')) return '';
        return `${u.pathname}${u.search}`;
    } catch {
        return s.startsWith('/api') ? s : '';
    }
};

const toAbsoluteApiUrl = (base, apiPath) => {
    const b = String(base || '').trim().replace(/\/+$/, '');
    if (!b) return apiPath;
    if (b.endsWith('/api') && apiPath === '/api') return b;
    if (b.endsWith('/api') && apiPath.startsWith('/api/')) return `${b}${apiPath.slice(4)}`;
    return `${b}${apiPath}`;
};

const isApiRequestUrl = (value) => {
    const s = String(value || '').trim().toLowerCase();
    if (!s) return false;
    return s.endsWith('/api') || s.includes('/api/');
};

const joinWithApiBase = (path) => {
    if (!configuredApiBase) return path;
    if (configuredApiBase.endsWith('/api') && path === '/api') return configuredApiBase;
    if (configuredApiBase.endsWith('/api') && path.startsWith('/api/')) return `${configuredApiBase}${path.slice(4)}`;
    return `${configuredApiBase}${path}`;
};

const normalizeApiUrl = (value) => {
    const s = String(value || '').trim();
    if (!s) return s;

    if (!configuredApiBase) return s;

    if (s.startsWith('https://localhost:44357')) {
        return joinWithApiBase(s.slice('https://localhost:44357'.length));
    }
    if (s.startsWith('http://localhost:44357')) {
        return joinWithApiBase(s.slice('http://localhost:44357'.length));
    }
    if (s.startsWith('/api')) {
        return joinWithApiBase(s);
    }
    return s;
};

const normalizeFetchInput = (input) => {
    if (typeof input === 'string') return normalizeApiUrl(input);
    if (input instanceof URL) return new URL(normalizeApiUrl(input.toString()));
    if (input instanceof Request) {
        const normalizedUrl = normalizeApiUrl(input.url);
        if (normalizedUrl === input.url) return input;
        return new Request(normalizedUrl, input);
    }
    return input;
};

const getRequestMethod = (input, init = {}) => {
    const raw = init?.method || (input instanceof Request ? input.method : 'GET');
    return String(raw || 'GET').trim().toUpperCase();
};

const isAbortLikeError = (error, signal = null) => {
    if (signal?.aborted) return true;
    const name = String(error?.name || '').trim();
    const code = String(error?.code || '').trim();
    if (name === 'AbortError' || name === 'CanceledError') return true;
    if (code === 'ERR_CANCELED') return true;
    const message = String(error?.message || '').toLowerCase();
    return message.includes('aborted') || message.includes('canceled') || message.includes('cancelled');
};

const getOfflineApiResponse = (method) => {
    if (method === 'GET') {
        return { status: 200, body: [] };
    }
    return { status: 503, body: { error: 'API is disabled in frontend mode.' } };
};

const getRuntimeAuth = () => {
    return {
        activationToken: localStorage.getItem('activationToken') || '',
        appApiKey: localStorage.getItem('appApiKey') || import.meta.env.VITE_APP_API_KEY || '',
        correlationId: getClientCorrelationId()
    };
};

function emitRuntimeError(detail) {
    try {
        window.dispatchEvent(new CustomEvent('etdp:runtime-error', { detail }));
    } catch {
        // Non-critical: diagnostics event channel should not break runtime.
    }
}

window.fetch = (input, init = {}) => {
    const normalizedInput = normalizeFetchInput(input);
    const targetUrl = toUrlString(normalizedInput);
    const isApiRequest = isApiRequestUrl(targetUrl);
    const method = getRequestMethod(input, init);

    if (apiDisabled && isApiRequest) {
        const offline = getOfflineApiResponse(method);
        return Promise.resolve(new Response(JSON.stringify(offline.body), {
            status: offline.status,
            headers: { 'Content-Type': 'application/json' }
        }));
    }

    const headers = new Headers(init.headers || {});
    const { activationToken, appApiKey, correlationId } = getRuntimeAuth();

    if (isApiRequest && activationToken && !headers.has('X-Activation-Token')) {
        headers.set('X-Activation-Token', activationToken);
    }
    if (isApiRequest && appApiKey && !headers.has('X-App-Api-Key')) {
        headers.set('X-App-Api-Key', appApiKey);
    }
    if (isApiRequest && !headers.has('X-Client-Correlation-Id')) {
        headers.set('X-Client-Correlation-Id', correlationId);
    }

    const requestInit = { ...init, headers };
    const shouldTryDevFallback =
        import.meta.env.DEV &&
        method === 'GET' &&
        isApiRequest &&
        isSameOriginApiRequest(normalizedInput);

    const tryFallbackFetch = async () => {
        if (!shouldTryDevFallback) return null;
        const apiPath = getApiPathFromUrl(normalizedInput);
        if (!apiPath) return null;

        for (const base of devApiFallbackBases) {
            try {
                const retryRes = await originalFetch(toAbsoluteApiUrl(base, apiPath), requestInit);
                if (retryRes.ok || retryRes.status < 500) return retryRes;
            } catch (fallbackError) {
                if (isAbortLikeError(fallbackError, requestInit?.signal)) {
                    throw fallbackError;
                }
                // Try next fallback base.
            }
        }

        return null;
    };

    return originalFetch(normalizedInput, requestInit)
        .then(async (response) => {
            if (!shouldTryDevFallback || response.status < 500) return response;
            const fallbackResponse = await tryFallbackFetch();
            return fallbackResponse || response;
        })
        .catch(async (error) => {
            if (isAbortLikeError(error, requestInit?.signal)) {
                throw error;
            }
            const fallbackResponse = await tryFallbackFetch();
            if (fallbackResponse) return fallbackResponse;

            if (import.meta.env.DEV && isApiRequest && method === 'GET') {
                emitRuntimeError({
                    source: 'fetch.dev-fallback',
                    message: `GET API request failed: ${targetUrl}`,
                    stack: error?.stack || null
                });
                return new Response(JSON.stringify({
                    error: 'GET API request failed.',
                    message: String(error?.message || error || 'Unknown fetch error'),
                    url: targetUrl
                }), {
                    status: 503,
                    headers: { 'Content-Type': 'application/json' }
                });
            }
            throw error;
        });
};

const hasHeader = (headers, name) => {
    if (!headers) return false;
    if (typeof headers.has === 'function') return headers.has(name);
    const n = name.toLowerCase();
    return Object.keys(headers).some((k) => k.toLowerCase() === n);
};

const setHeader = (headers, name, value) => {
    if (!headers || !value) return;
    if (typeof headers.set === 'function') {
        headers.set(name, value);
        return;
    }
    headers[name] = value;
};

axios.interceptors.request.use((config) => {
    const next = { ...config };
    if (typeof next.url === 'string') {
        next.url = normalizeApiUrl(next.url);
    }

    next.headers = next.headers || {};
    const { activationToken, appApiKey, correlationId } = getRuntimeAuth();
    const base = typeof next.baseURL === 'string' ? normalizeApiUrl(next.baseURL) : '';
    if (base) next.baseURL = base;
    const url = toUrlString(next.url);
    const resolvedUrl = url.startsWith('http://') || url.startsWith('https://') ? url : `${base}${url}`;
    const isApiRequest = isApiRequestUrl(resolvedUrl);

    if (apiDisabled && isApiRequest) {
        const method = String(next.method || 'get').trim().toUpperCase();
        const offline = getOfflineApiResponse(method);
        next.adapter = async () => ({
            data: offline.body,
            status: offline.status,
            statusText: offline.status === 200 ? 'OK' : 'Service Unavailable',
            headers: {},
            config: next,
            request: null
        });
        return next;
    }

    if (isApiRequest && activationToken && !hasHeader(next.headers, 'X-Activation-Token')) {
        setHeader(next.headers, 'X-Activation-Token', activationToken);
    }
    if (isApiRequest && appApiKey && !hasHeader(next.headers, 'X-App-Api-Key')) {
        setHeader(next.headers, 'X-App-Api-Key', appApiKey);
    }
    if (isApiRequest && !hasHeader(next.headers, 'X-Client-Correlation-Id')) {
        setHeader(next.headers, 'X-Client-Correlation-Id', correlationId);
    }

    return next;
});

axios.interceptors.response.use(
    (response) => response,
    async (error) => {
        if (isAbortLikeError(error)) throw error;
        if (!import.meta.env.DEV) throw error;

        const config = error?.config;
        if (!config || config.__devProxyRetried) throw error;

        const method = String(config.method || 'get').trim().toUpperCase();
        if (method !== 'GET') throw error;

        const status = Number(error?.response?.status || 0);
        if (status > 0 && status < 500) throw error;

        const base = typeof config.baseURL === 'string' ? config.baseURL : '';
        const url = toUrlString(config.url);
        const resolvedUrl = isAbsoluteHttpUrl(url) ? url : `${base}${url}`;

        if (!isApiRequestUrl(resolvedUrl) || !isSameOriginApiRequest(resolvedUrl)) throw error;

        const apiPath = getApiPathFromUrl(resolvedUrl);
        if (!apiPath) throw error;

        for (const fallbackBase of devApiFallbackBases) {
            try {
                return await axios.request({
                    ...config,
                    __devProxyRetried: true,
                    baseURL: '',
                    url: toAbsoluteApiUrl(fallbackBase, apiPath)
                });
            } catch (retryError) {
                if (isAbortLikeError(retryError, config?.signal)) throw retryError;
                // Try next fallback base.
            }
        }

        emitRuntimeError({
            source: 'axios.dev-fallback',
            message: `GET API request failed: ${resolvedUrl}`,
            stack: error?.stack || null
        });
        throw error;
    }
);

const originalOpen = window.open.bind(window);
window.open = (url, target, features) => {
    const normalizedUrl = typeof url === 'string' ? normalizeApiUrl(url) : url;
    return originalOpen(normalizedUrl, target, features);
};

window.addEventListener('error', (event) => {
    if (isAbortLikeError(event?.error || { message: event?.message })) return;

    const detail = {
        source: 'window.error',
        message: event?.message || 'Unhandled client error',
        stack: event?.error?.stack || null,
        fileName: event?.filename || null,
        lineNo: event?.lineno || null,
        colNo: event?.colno || null
    };

    reportClientError({
        severity: 'error',
        source: detail.source,
        message: detail.message,
        stack: detail.stack,
        url: window.location.href,
        metadata: {
            fileName: detail.fileName,
            lineNo: detail.lineNo,
            colNo: detail.colNo
        }
    });
    emitRuntimeError(detail);
});

window.addEventListener('unhandledrejection', (event) => {
    const reason = event?.reason;
    if (isAbortLikeError(reason)) return;

    const detail = {
        source: 'window.unhandledrejection',
        message: typeof reason === 'string' ? reason : (reason?.message || 'Unhandled promise rejection'),
        stack: reason?.stack || null
    };

    reportClientError({
        severity: 'error',
        source: detail.source,
        message: detail.message,
        stack: detail.stack,
        url: window.location.href
    });
    emitRuntimeError(detail);
});

ReactDOM.createRoot(document.getElementById('root')).render(
    <React.StrictMode>
        <BrowserRouter>
            <QualificationProvider>
                <GlossaryProvider>
                    <App />
                </GlossaryProvider>
            </QualificationProvider>
        </BrowserRouter>
    </React.StrictMode>
);
