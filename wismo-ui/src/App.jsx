import { useEffect, useMemo, useState } from 'react'
import { API_BASE_URL } from './config/api.js'
import { useStoreSession } from './hooks/useStoreSession.jsx'
import apiClient from './lib/apiClient.js'
import { createDashboardHubConnection } from './lib/signalrClient.js'

function App() {
  const {
    accessToken,
    email: sessionEmail,
    isAuthenticated,
    login,
    logout,
    tenantId,
    userName
  } = useStoreSession()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [shopDomain, setShopDomain] = useState('')
  const [authError, setAuthError] = useState(null)
  const [isLoggingIn, setIsLoggingIn] = useState(false)

  const [tickets, setTickets] = useState([])
  const [summary, setSummary] = useState(null)
  const [loading, setLoading] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [lastUpdatedAt, setLastUpdatedAt] = useState(null)
  const [realtimeState, setRealtimeState] = useState('disconnected')
  const [error, setError] = useState(null)

  const ticketRows = useMemo(() => {
    return tickets.map(ticket => ({
      ticket,
      awb: parseAwbReference(ticket.orderNumber)
    }))
  }, [tickets])

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    const url = new URL(window.location.href)
    const oauthError = url.searchParams.get('oauthError')

    if (!oauthError) {
      return
    }

    setAuthError(oauthError)
    url.searchParams.delete('oauthError')

    const cleanUrl = `${url.pathname}${url.searchParams.toString() ? `?${url.searchParams.toString()}` : ''}${url.hash}`
    window.history.replaceState({}, document.title, cleanUrl)
  }, [])

  useEffect(() => {
    if (!isAuthenticated || !accessToken) {
      setTickets([])
      setSummary(null)
      setLastUpdatedAt(null)
      setIsRefreshing(false)
      setLoading(false)
      setRealtimeState('disconnected')
      return
    }

    let isDisposed = false
    let inFlight = false

    const connection = createDashboardHubConnection(accessToken)

    async function loadDashboardData({ background = false } = {}) {
      if (inFlight) {
        return
      }

      inFlight = true

      if (background) {
        setIsRefreshing(true)
      } else {
        setLoading(true)
      }

      try {
        const [summaryResponse, ticketsResponse] = await Promise.all([
          apiClient.get('/api/dashboard/summary'),
          apiClient.get('/api/tickets')
        ])

        if (isDisposed) {
          return
        }

        setSummary(summaryResponse.data)
        setTickets(ticketsResponse.data)
        setLastUpdatedAt(new Date())
        setError(null)
      } catch (err) {
        if (!isDisposed && err.code !== 'ERR_CANCELED') {
          setError(err.response?.data?.message ?? err.message ?? 'Nu am putut incarca dashboard-ul.')
        }
      } finally {
        if (!isDisposed) {
          if (background) {
            setIsRefreshing(false)
          } else {
            setLoading(false)
          }
        }

        inFlight = false
      }
    }

    function handleTenantDashboardUpdated() {
      void loadDashboardData({ background: true })
    }

    connection.on('TenantDashboardUpdated', handleTenantDashboardUpdated)

    connection.onreconnecting(() => {
      if (!isDisposed) {
        setRealtimeState('reconnecting')
      }
    })

    connection.onreconnected(() => {
      if (!isDisposed) {
        setRealtimeState('connected')
        void loadDashboardData({ background: true })
      }
    })

    connection.onclose(() => {
      if (!isDisposed) {
        setRealtimeState('disconnected')
      }
    })

    async function startRealtime() {
      setRealtimeState('connecting')

      try {
        await connection.start()

        if (!isDisposed) {
          setRealtimeState('connected')
        }
      } catch (err) {
        if (!isDisposed) {
          setRealtimeState('disconnected')
          setError(err?.message ?? 'Conexiunea realtime nu a putut fi pornita.')
        }
      }
    }

    void loadDashboardData()
    void startRealtime()

    return () => {
      isDisposed = true
      connection.off('TenantDashboardUpdated', handleTenantDashboardUpdated)
      void connection.stop()
    }
  }, [isAuthenticated, accessToken])

  async function handleLogin(event) {
    event.preventDefault()
    setIsLoggingIn(true)
    setAuthError(null)

    try {
      await login(email, password)
    } catch (err) {
      setAuthError(err.message)
    } finally {
      setIsLoggingIn(false)
    }
  }

  function handleShopifyInstall() {
    const normalizedShop = shopDomain.trim().toLowerCase()

    if (!normalizedShop) {
      setAuthError('Introdu domeniul magazinului Shopify (ex: demo-store.myshopify.com).')
      return
    }

    const installUrl = `${API_BASE_URL}/api/shopify/connect/install?shop=${encodeURIComponent(normalizedShop)}`
    window.location.assign(installUrl)
  }

  if (!isAuthenticated) {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', background: '#f8fafc', fontFamily: 'sans-serif' }}>
        <form
          onSubmit={handleLogin}
          style={{ background: '#fff', borderRadius: 12, padding: 24, width: '100%', maxWidth: 480, boxShadow: '0 14px 40px rgba(15,23,42,0.12)' }}
        >
          <h1 style={{ marginTop: 0, marginBottom: 8, color: '#0f172a' }}>WISMO Dashboard Login</h1>
          <p style={{ marginTop: 0, marginBottom: 16, color: '#475569' }}>Autentificare pe tenant prin JWT sau instalare Shopify 1-click.</p>

          <label style={{ display: 'block', marginBottom: 6, color: '#334155', fontSize: 14 }}>Email</label>
          <input
            value={email}
            onChange={event => setEmail(event.target.value)}
            type='email'
            required
            style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid #cbd5e1', marginBottom: 12 }}
          />

          <label style={{ display: 'block', marginBottom: 6, color: '#334155', fontSize: 14 }}>Parola</label>
          <input
            value={password}
            onChange={event => setPassword(event.target.value)}
            type='password'
            required
            style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid #cbd5e1', marginBottom: 12 }}
          />

          {authError && (
            <div style={{ background: '#fee2e2', color: '#991b1b', borderRadius: 8, padding: 10, marginBottom: 12 }}>
              {authError}
            </div>
          )}

          <button
            type='submit'
            disabled={isLoggingIn}
            style={{ width: '100%', padding: 11, borderRadius: 8, border: 'none', cursor: 'pointer', background: '#2563eb', color: '#fff', fontWeight: 600 }}
          >
            {isLoggingIn ? 'Autentificare...' : 'Login'}
          </button>

          <div style={{ margin: '18px 0', borderTop: '1px solid #e2e8f0' }} />

          <label style={{ display: 'block', marginBottom: 6, color: '#334155', fontSize: 14 }}>Shop domain (Shopify)</label>
          <input
            value={shopDomain}
            onChange={event => setShopDomain(event.target.value)}
            type='text'
            placeholder='demo-store.myshopify.com'
            style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid #cbd5e1', marginBottom: 12 }}
          />

          <button
            type='button'
            onClick={handleShopifyInstall}
            style={{ width: '100%', padding: 11, borderRadius: 8, border: '1px solid #0f172a', cursor: 'pointer', background: '#fff', color: '#0f172a', fontWeight: 600 }}
          >
            Install Shopify App (1-click)
          </button>
        </form>
      </div>
    )
  }

  return (
    <div style={{ padding: '2rem', fontFamily: 'sans-serif', maxWidth: '1100px', margin: '0 auto' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <div>
          <h1 style={{ margin: 0, color: '#0f172a' }}>WISMO Dashboard</h1>
          <p style={{ margin: '6px 0 0', color: '#475569' }}>
            Logged in as <strong>{userName}</strong> ({sessionEmail}) | Tenant {tenantId}
          </p>
        </div>
        <button
          onClick={logout}
          style={{ padding: '10px 14px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#fff', cursor: 'pointer' }}
        >
          Logout
        </button>
      </div>

      {loading && <div style={{ marginBottom: 12 }}>Incarcare dashboard...</div>}
      {error && <div style={{ marginBottom: 12, color: '#b91c1c' }}>Eroare: {error}</div>}

      {summary && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, minmax(120px, 1fr))', gap: 10, marginBottom: 24 }}>
          <StatCard label='Total' value={summary.totalTickets} />
          <StatCard label='In Transit' value={summary.inTransit} />
          <StatCard label='RequiresApproval' value={summary.requiresApproval} />
          <StatCard label='Delivered' value={summary.delivered} />
          <StatCard label='DeliveryIssue' value={summary.deliveryIssue} />
          <StatCard label='Other' value={summary.other} />
        </div>
      )}

      <div style={{ background: '#fff', border: '1px solid #e2e8f0', borderRadius: 12, overflow: 'hidden' }}>
        <div style={{ padding: '14px 16px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <div>
            <h2 style={{ margin: 0, color: '#0f172a' }}>AWB Tracking Live</h2>
            <p style={{ margin: '4px 0 0', color: '#64748b', fontSize: 13 }}>
              Actualizari on-demand prin SignalR, izolate pe tenant.
            </p>
          </div>
          <div style={{ color: '#475569', fontSize: 13, display: 'flex', alignItems: 'center', gap: 8 }}>
            <span>{lastUpdatedAt ? `Ultimul refresh: ${lastUpdatedAt.toLocaleTimeString('ro-RO')}` : 'Astept primul refresh...'}</span>
            <span style={getRealtimeBadgeStyle(realtimeState)}>{getRealtimeLabel(realtimeState)}</span>
            {isRefreshing && <span style={{ color: '#2563eb', fontWeight: 600 }}>Actualizare...</span>}
          </div>
        </div>

        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 760 }}>
            <thead>
              <tr style={{ background: '#f8fafc' }}>
                <th style={headerCellStyle}>Ticket</th>
                <th style={headerCellStyle}>Courier</th>
                <th style={headerCellStyle}>AWB</th>
                <th style={headerCellStyle}>Status</th>
                <th style={headerCellStyle}>Client</th>
                <th style={headerCellStyle}>Store</th>
              </tr>
            </thead>
            <tbody>
              {ticketRows.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ padding: 18, textAlign: 'center', color: '#64748b' }}>
                    Nu exista inca ticket-uri pentru tenantul curent.
                  </td>
                </tr>
              )}

              {ticketRows.map(({ ticket, awb }) => (
                <tr key={ticket.id} style={{ borderTop: '1px solid #f1f5f9' }}>
                  <td style={bodyCellStyle}>#{ticket.id}</td>
                  <td style={bodyCellStyle}>{awb?.courier ?? 'UNKNOWN'}</td>
                  <td style={{ ...bodyCellStyle, fontFamily: 'monospace', fontWeight: 600 }}>{awb?.awb ?? ((ticket.orderNumber ?? '').trim() || 'N/A')}</td>
                  <td style={bodyCellStyle}>
                    <span style={getStatusBadgeStyle(ticket.status)}>{ticket.status}</span>
                  </td>
                  <td style={bodyCellStyle}>{ticket.customerEmail}</td>
                  <td style={bodyCellStyle}>{ticket.tenantName}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function parseAwbReference(orderNumber) {
  const raw = (orderNumber ?? '').trim()
  const match = raw.match(/^(SAMEDAY|FAN|CARGUS)\s*:\s*(.+)$/i)

  if (!match) {
    return null
  }

  return {
    courier: match[1].toUpperCase(),
    awb: match[2].trim()
  }
}

function getRealtimeLabel(state) {
  if (state === 'connected') {
    return 'Realtime conectat'
  }

  if (state === 'reconnecting') {
    return 'Realtime reconnecting'
  }

  if (state === 'connecting') {
    return 'Realtime conectare'
  }

  return 'Realtime deconectat'
}

function getRealtimeBadgeStyle(state) {
  if (state === 'connected') {
    return {
      ...badgeBaseStyle,
      background: '#dcfce7',
      color: '#166534'
    }
  }

  if (state === 'reconnecting' || state === 'connecting') {
    return {
      ...badgeBaseStyle,
      background: '#fef9c3',
      color: '#854d0e'
    }
  }

  return {
    ...badgeBaseStyle,
    background: '#fee2e2',
    color: '#991b1b'
  }
}

function getStatusBadgeStyle(status) {
  const normalized = (status ?? '').toLowerCase()

  if (normalized.includes('delivered')) {
    return {
      ...badgeBaseStyle,
      background: '#dcfce7',
      color: '#166534'
    }
  }

  if (normalized.includes('issue') || normalized.includes('failed') || normalized.includes('returned')) {
    return {
      ...badgeBaseStyle,
      background: '#fee2e2',
      color: '#991b1b'
    }
  }

  if (normalized.includes('transit')) {
    return {
      ...badgeBaseStyle,
      background: '#dbeafe',
      color: '#1e40af'
    }
  }

  return {
    ...badgeBaseStyle,
    background: '#e2e8f0',
    color: '#334155'
  }
}

const headerCellStyle = {
  textAlign: 'left',
  padding: '10px 12px',
  fontSize: 12,
  color: '#475569',
  textTransform: 'uppercase',
  letterSpacing: 0.5
}

const bodyCellStyle = {
  padding: '12px',
  color: '#0f172a',
  fontSize: 14,
  verticalAlign: 'middle'
}

const badgeBaseStyle = {
  display: 'inline-block',
  padding: '4px 10px',
  borderRadius: 9999,
  fontSize: 12,
  fontWeight: 700
}

function StatCard({ label, value }) {
  return (
    <div style={{ background: '#fff', border: '1px solid #e2e8f0', borderRadius: 10, padding: 12 }}>
      <div style={{ color: '#64748b', fontSize: 12, marginBottom: 4 }}>{label}</div>
      <div style={{ color: '#0f172a', fontWeight: 700, fontSize: 22 }}>{value ?? 0}</div>
    </div>
  )
}

export default App
