import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import apiClient from '../lib/apiClient.js'
import {
  SESSION_EXPIRED_EVENT,
  clearSessionFromStorage,
  loadSessionFromStorage,
  saveSessionToStorage
} from '../session/sessionStorage.js'

const StoreSessionContext = createContext(null)

export function StoreSessionProvider({ children }) {
  const [session, setSession] = useState(loadSessionFromStorage)

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    const url = new URL(window.location.href)
    const accessToken = url.searchParams.get('accessToken')

    if (!accessToken) {
      return
    }

    const tenantId = Number.parseInt(url.searchParams.get('tenantId') ?? '', 10)

    if (!Number.isFinite(tenantId) || tenantId <= 0) {
      return
    }

    const nextSession = {
      accessToken,
      expiresAtUtc: url.searchParams.get('expiresAtUtc') ?? null,
      tenantId,
      userName: url.searchParams.get('userName') ?? 'Shopify User',
      email: url.searchParams.get('email') ?? ''
    }

    setSession(nextSession)
    saveSessionToStorage(nextSession)

    const cleanupKeys = ['accessToken', 'expiresAtUtc', 'tenantId', 'userName', 'email', 'oauthError']
    cleanupKeys.forEach(key => url.searchParams.delete(key))

    const cleanUrl = `${url.pathname}${url.searchParams.toString() ? `?${url.searchParams.toString()}` : ''}${url.hash}`
    window.history.replaceState({}, document.title, cleanUrl)
  }, [])

  useEffect(() => {
    function handleSessionExpired() {
      setSession(null)
    }

    window.addEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired)

    return () => {
      window.removeEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired)
    }
  }, [])

  const login = useCallback(async (email, password) => {
    try {
      const response = await apiClient.post('/api/auth/login', {
        email,
        password
      })

      const payload = response.data

      const nextSession = {
        accessToken: payload.accessToken,
        expiresAtUtc: payload.expiresAtUtc,
        tenantId: payload.tenantId,
        userName: payload.userName,
        email: payload.email
      }

      setSession(nextSession)
      saveSessionToStorage(nextSession)

      return nextSession
    } catch (error) {
      if (error.response?.status === 401) {
        throw new Error('Email sau parola invalida.')
      }

      throw new Error('Nu am putut face login. Incearca din nou.')
    }
  }, [])

  const logout = useCallback(() => {
    setSession(null)
    clearSessionFromStorage()
  }, [])

  const value = useMemo(() => ({
    accessToken: session?.accessToken ?? null,
    tenantId: session?.tenantId ?? null,
    userName: session?.userName ?? null,
    email: session?.email ?? null,
    expiresAtUtc: session?.expiresAtUtc ?? null,
    isAuthenticated: Boolean(session?.accessToken),
    login,
    logout
  }), [session, login, logout])

  return (
    <StoreSessionContext.Provider value={value}>
      {children}
    </StoreSessionContext.Provider>
  )
}

export function useStoreSession() {
  const context = useContext(StoreSessionContext)

  if (!context) {
    throw new Error('useStoreSession must be used inside StoreSessionProvider')
  }

  return context
}
