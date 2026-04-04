import axios from 'axios'
import { API_BASE_URL } from '../config/api.js'
import {
  clearSessionFromStorage,
  getAccessTokenFromStorage,
  notifySessionExpired
} from '../session/sessionStorage.js'

const apiClient = axios.create({
  baseURL: API_BASE_URL
})

apiClient.interceptors.request.use(config => {
  if (config.url?.includes('/api/auth/login')) {
    return config
  }

  const accessToken = getAccessTokenFromStorage()

  if (!accessToken) {
    return config
  }

  if (typeof config.headers?.set === 'function') {
    config.headers.set('Authorization', `Bearer ${accessToken}`)
    return config
  }

  config.headers = {
    ...(config.headers ?? {}),
    Authorization: `Bearer ${accessToken}`
  }

  return config
})

apiClient.interceptors.response.use(
  response => response,
  error => {
    const statusCode = error.response?.status
    const requestUrl = error.config?.url ?? ''

    if (statusCode === 401 && !requestUrl.includes('/api/auth/login')) {
      clearSessionFromStorage()
      notifySessionExpired()
    }

    return Promise.reject(error)
  }
)

export default apiClient
