import { describe, it, expect, beforeEach, vi } from 'vitest'
import axios from 'axios'

// Mock sessionStorage module before importing apiClient
vi.mock('../session/sessionStorage.js', () => ({
  getAccessTokenFromStorage: vi.fn(() => null),
  clearSessionFromStorage: vi.fn(),
  notifySessionExpired: vi.fn(),
}))

import { getAccessTokenFromStorage, clearSessionFromStorage, notifySessionExpired } from '../session/sessionStorage.js'

describe('apiClient', () => {
  let apiClient

  beforeEach(async () => {
    vi.resetModules()
    vi.clearAllMocks()

    // Re-mock before re-importing
    vi.mock('../session/sessionStorage.js', () => ({
      getAccessTokenFromStorage: vi.fn(() => null),
      clearSessionFromStorage: vi.fn(),
      notifySessionExpired: vi.fn(),
    }))

    const mod = await import('./apiClient.js')
    apiClient = mod.default
  })

  it('has correct baseURL', () => {
    expect(apiClient.defaults.baseURL).toBe('http://localhost:5255')
  })

  it('has request and response interceptors', () => {
    expect(apiClient.interceptors.request.handlers.length).toBeGreaterThan(0)
    expect(apiClient.interceptors.response.handlers.length).toBeGreaterThan(0)
  })
})
