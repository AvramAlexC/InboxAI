import { describe, it, expect } from 'vitest'
import { API_BASE_URL } from './api.js'

describe('api config', () => {
  it('exports a non-empty base URL', () => {
    expect(API_BASE_URL).toBeTruthy()
    expect(typeof API_BASE_URL).toBe('string')
  })
})
