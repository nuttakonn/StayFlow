// In production (Vercel) set VITE_API_BASE_URL=https://your-app.onrender.com
// In development leave it unset — Vite proxy handles /api → localhost:5090
export const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '';

const TOKEN_KEY = 'stayflow_auth_token';
const ORG_KEY = 'stayflow_active_org_id';
const USER_KEY = 'stayflow_user';

export interface UserSession {
  token: string;
  refreshToken: string;
  organizationId: string | null;
  user: {
    id: string;
    email: string;
    fullName: string;
    preferredLanguage?: string;
  };
}

export function getStoredSession(): UserSession | null {
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;

  const orgId = localStorage.getItem(ORG_KEY);
  const userStr = localStorage.getItem(USER_KEY);
  const user = userStr ? JSON.parse(userStr) : null;

  return {
    token,
    refreshToken: '',
    organizationId: orgId,
    user: user || { id: '', email: 'demo@stayflow.com', fullName: 'Staff User' },
  };
}

export function saveSession(token: string, refreshToken: string, orgId?: string | null, user?: unknown) {
  localStorage.setItem(TOKEN_KEY, token);
  if (refreshToken) localStorage.setItem('stayflow_refresh_token', refreshToken);
  if (orgId) localStorage.setItem(ORG_KEY, orgId);
  if (user) localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(ORG_KEY);
  localStorage.removeItem(USER_KEY);
}

export function updateStoredLanguage(lang: string) {
  const userStr = localStorage.getItem(USER_KEY);
  if (!userStr) return;
  try {
    const user = JSON.parse(userStr);
    user.preferredLanguage = lang;
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  } catch { /* ignore */ }
}

/** Legacy: returns raw Response (used by App.tsx) */
export async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  const session = getStoredSession();
  const headers = new Headers(options.headers || {});

  if (session?.token) {
    headers.set('Authorization', `Bearer ${session.token}`);
  }
  if (session?.organizationId) {
    headers.set('X-Organization-Id', session.organizationId);
  }

  const response = await fetch(API_BASE + url, {
    ...options,
    headers,
  });

  return response;
}

/**
 * Phase 3+: Returns parsed JSON. Throws Error with message on non-OK.
 * Signature: apiFetch(url, token, orgId, options?)
 */
export async function apiFetch<T = unknown>(
  url: string,
  token: string,
  orgId: string,
  options: RequestInit = {}
): Promise<T> {
  const headers = new Headers(options.headers || {});
  headers.set('Content-Type', 'application/json');
  headers.set('Authorization', `Bearer ${token}`);
  headers.set('X-Organization-Id', orgId);

  const response = await fetch(API_BASE + url, { ...options, headers });

  if (!response.ok) {
    let msg = `HTTP ${response.status}`;
    try {
      const err = await response.json();
      if (err?.errors && typeof err.errors === 'object') {
        const errorList = Object.values(err.errors).flat().join('; ');
        msg = errorList || err?.title || msg;
      } else {
        msg = err?.detail ?? err?.title ?? msg;
      }
    } catch { /* ignore */ }
    throw new Error(msg);
  }

  // 204 No Content
  if (response.status === 204) return undefined as T;

  return response.json() as Promise<T>;
}

