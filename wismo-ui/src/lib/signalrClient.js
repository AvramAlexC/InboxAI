import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { API_BASE_URL } from '../config/api.js'

const DASHBOARD_HUB_URL = `${API_BASE_URL}/hubs/dashboard`

export function createDashboardHubConnection(accessToken) {
  return new HubConnectionBuilder()
    .withUrl(DASHBOARD_HUB_URL, {
      accessTokenFactory: () => accessToken ?? ''
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()
}
