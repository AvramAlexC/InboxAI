import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  SESSION_STORAGE_KEY,
  SESSION_EXPIRED_EVENT,
  loadSessionFromStorage,
  saveSessionToStorage,
  clearSessionFromStorage,
  getAccessTokenFromStorage,
  notifySessionExpired,
} from './sessionStorage.js'

describe('sessionStorage', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  describe('loadSessionFromStorage', () => {
    it('returns null when no session exists', () => {
      expect(loadSessionFromStorage()).toBeNull()
    })

    it('returns parsed session when it exists', () => {
      const session = { accessToken: 'abc', userName: 'Test' }
      localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session))

      expect(loadSessionFromStorage()).toEqual(session)
    })

    it('returns null when stored value is invalid JSON', () => {
      localStorage.setItem(SESSION_STORAGE_KEY, 'not-json')

      expect(loadSessionFromStorage()).toBeNull()
    })
  })

  describe('saveSessionToStorage', () => {
    it('saves session to localStorage', () => {
      const session = { accessToken: 'token123', email: 'user@test.com' }
      saveSessionToStorage(session)

      const stored = JSON.parse(localStorage.getItem(SESSION_STORAGE_KEY))
      expect(stored).toEqual(session)
    })
  })

  describe('clearSessionFromStorage', () => {
    it('removes session from localStorage', () => {
      localStorage.setItem(SESSION_STORAGE_KEY, '"data"')
      clearSessionFromStorage()

      expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull()
    })
  })

  describe('getAccessTokenFromStorage', () => {
    it('returns null when no session exists', () => {
      expect(getAccessTokenFromStorage()).toBeNull()
    })

    it('returns accessToken from stored session', () => {
      saveSessionToStorage({ accessToken: 'my-token' })

      expect(getAccessTokenFromStorage()).toBe('my-token')
    })

    it('returns null when session has no accessToken', () => {
      saveSessionToStorage({ email: 'user@test.com' })

      expect(getAccessTokenFromStorage()).toBeNull()
    })
  })

  describe('notifySessionExpired', () => {
    it('dispatches session expired event', () => {
      const handler = vi.fn()
      window.addEventListener(SESSION_EXPIRED_EVENT, handler)

      notifySessionExpired()

      expect(handler).toHaveBeenCalledOnce()
      window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
    })
  })
})
