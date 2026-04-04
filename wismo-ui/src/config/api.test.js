import { describe, it, expect } from 'vitest'
import { API_BASE_URL } from './api.js'

describe('api config', () => {
  it('exports the correct base URL', () => {
    expect(API_BASE_URL).toBe('http://localhost:5255')
  })
})
