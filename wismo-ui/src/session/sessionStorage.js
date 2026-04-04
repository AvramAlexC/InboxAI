export const SESSION_STORAGE_KEY = 'wismo.session'
export const SESSION_EXPIRED_EVENT = 'wismo:session-expired'

export function loadSessionFromStorage() {
  try {
    const rawValue = localStorage.getItem(SESSION_STORAGE_KEY)

    if (!rawValue) {
      return null
    }

    return JSON.parse(rawValue)
  } catch {
    return null
  }
}

export function saveSessionToStorage(session) {
  localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session))
}

export function clearSessionFromStorage() {
  localStorage.removeItem(SESSION_STORAGE_KEY)
}

export function getAccessTokenFromStorage() {
  return loadSessionFromStorage()?.accessToken ?? null
}

export function notifySessionExpired() {
  if (typeof window === 'undefined') {
    return
  }

  window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT))
}
