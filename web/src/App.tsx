import { Fragment, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { api, API_BASE_URL } from './api';
import type {
  AttemptPage,
  AttemptSummary,
  ClientView,
  TicketSummaryView,
  AccessMessageView,
  AccessMessageTemplateView,
  SessionView,
  UserView,
  RoleView,
  PermissionView,
  DeviceType,
  DeviceView,
  EventView,
  GateSectorView,
  GateView,
  Screen,
  SectorView,
  TicketView,
  VenueView,
} from './types';

const emptySummary: AttemptSummary = {
  eventId: '',
  totalAttempts: 0,
  approved: 0,
  rejected: 0,
  approvalRate: 0,
  byCredentialType: [],
  byDirection: [],
  byDecision: [],
  byGate: [],
  byReason: [],
  bySector: [],
};

const screenLabels: Record<Screen, { label: string; icon: string; description: string; group: 'op' | 'cadastro' | 'operacao' | 'logs' | 'admin'; perm: string | null }> = {
  dashboard:     { label: 'Dashboard',          icon: '⌂', description: 'Visão operacional dos seus eventos',              group: 'op',       perm: 'relatorio.ler' },
  clients:       { label: 'Clientes',            icon: '◧', description: 'Organizadores e clientes vinculados aos eventos', group: 'cadastro', perm: 'cliente.gerenciar' },
  events:        { label: 'Eventos',             icon: '◈', description: 'Agenda e configuração de eventos',                group: 'cadastro', perm: 'evento.criar' },
  configuration: { label: 'Portarias e Setores', icon: '⚙', description: 'Portarias, setores e regras de circulação',       group: 'operacao', perm: 'portaria.gerenciar' },
  manualValidation: { label: 'Validação Manual', icon: '✋', description: 'Liberação manual de acesso (backstage, exceções, falhas)', group: 'operacao', perm: 'acesso.validar' },
  tickets:       { label: 'Tickets',             icon: '▣', description: 'Ingressos emitidos e utilização',                 group: 'operacao', perm: 'ticket.consultar' },
  reports:       { label: 'Relatórios',          icon: '◈', description: 'Métricas, cobertura e análise de rejeições',      group: 'operacao', perm: 'relatorio.ler' },
  audit:         { label: 'Auditoria de acessos', icon: '◌', description: 'Histórico de tentativas de validação de acesso', group: 'logs',     perm: 'acessos.ler' },
  importLogs:    { label: 'Importações',         icon: '☰', description: 'Histórico de importações, com detalhe por linha', group: 'logs',     perm: 'ticket.importar' },
  import:        { label: 'Importar',            icon: '↑', description: 'Importar ingressos via arquivo CSV',              group: 'admin',    perm: 'ticket.importar' },
  users:         { label: 'Usuários',            icon: '◎', description: 'Usuários, perfis e permissões de acesso',         group: 'admin',    perm: 'usuario.gerenciar' },
  messages:      { label: 'Mensagens',           icon: '✉', description: 'Mensagens exibidas nas liberações e rejeições',   group: 'admin',    perm: 'mensagem.gerenciar' },
  admin:         { label: 'Dados do Evento',     icon: '⚠', description: 'Movido para dentro do cadastro de eventos',      group: 'admin',    perm: 'evento.excluir' },
};

function formatDate(value?: string | null, withTime = false) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('pt-BR', withTime
    ? { dateStyle: 'short', timeStyle: 'short' }
    : { dateStyle: 'medium' }).format(date);
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    Draft: 'Rascunho', Preparing: 'Preparando', Published: 'Publicado',
    Running: 'Em andamento', Closed: 'Encerrado', Archived: 'Arquivado',
    active: 'Ativo', inactive: 'Inativo', Approved: 'Aprovado', Rejected: 'Rejeitado',
  };
  return labels[status] ?? status;
}

function StatusBadge({ value }: { value: string }) {
  const normalized = value.toLowerCase();
  const tone = normalized.includes('approv') || normalized === 'active' || normalized === 'published' || normalized === 'running'
    ? 'positive'
    : normalized.includes('reject') || normalized === 'inactive' || normalized === 'closed'
      ? 'negative'
      : 'neutral';
  return <span className={`status-badge ${tone}`}><span className="status-dot" />{statusLabel(value)}</span>;
}

function MetricCard({ label, value, detail, tone }: { label: string; value: string | number; detail: string; tone: string }) {
  return (
    <article className="metric-card">
      <div className={`metric-icon ${tone}`}>{tone === 'blue' ? '↗' : tone === 'green' ? '✓' : tone === 'red' ? '!' : '%'}</div>
      <div>
        <p>{label}</p>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </article>
  );
}

function EmptyState({ message }: { message: string }) {
  return <div className="empty-state"><span>⌁</span><p>{message}</p></div>;
}

const navGroups: { key: string; label: string; items: Screen[] }[] = [
  { key: 'cadastro', label: 'CADASTRO', items: ['clients', 'events'] },
  { key: 'operacao', label: 'OPERAÇÃO', items: ['configuration', 'manualValidation', 'tickets', 'reports'] },
  { key: 'logs', label: 'LOGS', items: ['audit', 'importLogs'] },
  { key: 'admin', label: 'ADMINISTRAÇÃO', items: ['import', 'users', 'messages'] },
];

function App() {
  const [session, setSession] = useState<SessionView | null | undefined>(undefined); // undefined = carregando
  const [screen, setScreen] = useState<Screen>('dashboard');
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>(
    () => Object.fromEntries(navGroups.map((g) => [g.key, true])), // todos abertos por padrão
  );

  /** Troca de tela garantindo que o grupo correspondente esteja expandido no menu. */
  function goToScreen(target: Screen) {
    setScreen(target);
    const group = navGroups.find((g) => g.items.includes(target));
    if (group) setOpenGroups((current) => ({ ...current, [group.key]: true }));
  }

  function toggleGroup(key: string) {
    setOpenGroups((current) => ({ ...current, [key]: !current[key] }));
  }

  /** Verifica se a sessão atual tem uma permissão. */
  const can = (perm: string | null) =>
    perm === null || (session != null && session.permissions.includes(perm));

  /** Primeira tela que o usuário pode acessar (usada como landing após login). */
  function firstAllowedScreen(): Screen {
    if (can(screenLabels.dashboard.perm) && can('relatorio.ler')) return 'dashboard';
    const order: Screen[] = ['manualValidation', 'tickets', 'reports', 'audit', 'events', 'clients', 'configuration', 'import', 'importLogs', 'users', 'messages'];
    return order.find((s) => can(screenLabels[s].perm)) ?? 'dashboard';
  }
  const [events, setEvents] = useState<EventView[]>([]);
  const [selectedEventId, setSelectedEventId] = useState('');
  const [summary, setSummary] = useState<AttemptSummary>(emptySummary);
  const [attempts, setAttempts] = useState<AttemptPage | null>(null);
  const [tickets, setTickets] = useState<TicketView[]>([]);
  const [loading, setLoading] = useState(true);
  const [operationalLoading, setOperationalLoading] = useState(false);
  const [ticketsLoading, setTicketsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedEvent = useMemo(
    () => events.find((event) => event.id === selectedEventId) ?? null,
    [events, selectedEventId],
  );

  useEffect(() => {
    // Verifica sessão existente ao carregar
    api.me()
      .then((s) => setSession(s))
      .catch(() => setSession(null));
  }, []);

  useEffect(() => {
    // Ao confirmar a sessão, leva o usuário para a primeira tela que ele pode acessar.
    if (session) setScreen(firstAllowedScreen());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session]);

  useEffect(() => {
    // Só carrega dados quando a sessão estiver confirmada
    if (!session) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    api.listEvents()
      .then((result) => {
        if (cancelled) return;
        setEvents(result);
        if (result.length > 0) setSelectedEventId(result[0].id);
      })
      .catch((reason: unknown) => {
        if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível conectar à API.');
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [session]);

  useEffect(() => {
    if (!selectedEventId || !session || !session.permissions.includes('relatorio.ler')) {
      setSummary(emptySummary);
      setAttempts(null);
      return;
    }
    let cancelled = false;
    setOperationalLoading(true);
    setError(null);
    Promise.all([api.getSummary(selectedEventId), api.listAttempts(selectedEventId)])
      .then(([summaryResult, attemptsResult]) => {
        if (cancelled) return;
        setSummary(summaryResult);
        setAttempts(attemptsResult);
      })
      .catch((reason: unknown) => {
        if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os indicadores.');
      })
      .finally(() => { if (!cancelled) setOperationalLoading(false); });
    return () => { cancelled = true; };
  }, [selectedEventId, session]);

  useEffect(() => {
    if (screen !== 'tickets' || !selectedEventId || !session || !session.permissions.includes('ticket.consultar')) return;
    let cancelled = false;
    setTicketsLoading(true);
    api.listTickets(selectedEventId)
      .then((result) => { if (!cancelled) setTickets(result); })
      .catch((reason: unknown) => {
        if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os tickets.');
      })
      .finally(() => { if (!cancelled) setTicketsLoading(false); });
    return () => { cancelled = true; };
  }, [screen, selectedEventId]);

  const current = screenLabels[screen];
  const approvalRate = summary.totalAttempts > 0
    ? `${summary.approvalRate.toFixed(1).replace('.', ',')}%`
    : '0%';

  return (
    <div className="app-shell">
      {session === undefined && (
        <div className="loading-panel" style={{ width: '100vw', height: '100vh' }}><span className="spinner" />Verificando sessão...</div>
      )}
      {session === null && (
        <LoginView onLogin={(s) => setSession(s)} />
      )}
      {session !== null && session !== undefined && (
      <><aside className="sidebar">
        <div className="brand"><img className="brand-logo" src="/fastpass-logo-white.png" alt="FastPass Acesso" /><span className="brand-version">V2 Console</span></div>
        <div className="workspace-label">OPERAÇÃO</div>
        <nav className="main-nav" aria-label="Navegação principal">
          {can(screenLabels['dashboard'].perm) && (
          <button key="dashboard" className={screen === 'dashboard' ? 'nav-item active' : 'nav-item'} onClick={() => goToScreen('dashboard')}>
            <span className="nav-icon">{screenLabels['dashboard'].icon}</span><span>{screenLabels['dashboard'].label}</span>
          </button>
          )}
          {navGroups.map((group) => {
            const isOpen = openGroups[group.key];
            const visibleItems = group.items.filter((item) => {
              const perm = screenLabels[item].perm;
              return perm === null || session.permissions.includes(perm);
            });
            if (visibleItems.length === 0) return null; // esconde grupos sem itens visíveis
            return (
              <div className="nav-group" key={group.key}>
                <button type="button" className="nav-group-label nav-group-toggle" onClick={() => toggleGroup(group.key)}>
                  <span>{group.label}</span>
                  <span className={`nav-group-caret${isOpen ? ' open' : ''}`}>▾</span>
                </button>
                {isOpen && visibleItems.map((item) => (
                  <button key={item} className={screen === item ? 'nav-item active' : 'nav-item'} onClick={() => goToScreen(item)}>
                    <span className="nav-icon">{screenLabels[item].icon}</span><span>{screenLabels[item].label}</span>
                    {item === 'audit' && attempts?.total ? <em>{attempts.total}</em> : null}
                  </button>
                ))}
              </div>
            );
          })}
        </nav>
        <div className="sidebar-footer"><div className="connection-indicator"><span />API local conectada</div><small>{API_BASE_URL || window.location.origin}</small><div className="user-card"><div className="avatar">{session.displayName.slice(0, 2).toUpperCase()}</div><div><strong>{session.displayName}</strong><span>{session.userName}</span></div><button className="icon-button" title="Sair" onClick={async () => { await api.logout(); setSession(null); }} style={{ marginLeft: 'auto', fontSize: 14 }}>⏻</button></div></div>
      </aside>

      <main className="main-content">
        <header className="topbar">
          <div className="breadcrumb"><span>FastPass</span><b>/</b><strong>{current.label}</strong></div>
          <div className="header-actions"><button className="icon-button" title="Notificações">♢<i /></button><div className="header-divider" /><button className="help-button">? <span>Ajuda</span></button></div>
        </header>

        <div className="page-content">
          <div className="page-heading">
            <div><p className="eyebrow">CENTRAL DE OPERAÇÕES</p><h1>{current.label}</h1><p className="page-description">{current.description}</p></div>
            {screen !== 'clients' && screen !== 'users' && <div className="event-selector"><label htmlFor="event-select">Evento selecionado</label><select id="event-select" value={selectedEventId} onChange={(event) => setSelectedEventId(event.target.value)} disabled={loading || events.length === 0}><option value="">Nenhum evento disponível</option>{events.map((event) => <option value={event.id} key={event.id}>{event.name}</option>)}</select></div>}
          </div>
          {error && <div className="alert-error"><strong>Não foi possível atualizar os dados.</strong><span>{error}</span><button onClick={() => window.location.reload()}>Tentar novamente</button></div>}
          {loading ? <div className="loading-panel"><span className="spinner" />Carregando eventos...</div> : !can(current.perm) ? (
            <section className="panel full-panel"><EmptyState message="Você não tem permissão para acessar esta área." /></section>
          ) : (
            <>
              {screen === 'dashboard' && <Dashboard selectedEvent={selectedEvent} onOpen={goToScreen} />}
              {screen === 'events' && <EventsView events={events} onCreated={(created) => { setEvents((currentEvents) => [created, ...currentEvents]); setSelectedEventId(created.id); }} />}
              {screen === 'tickets' && <TicketsView tickets={tickets} loading={ticketsLoading} hasEvent={Boolean(selectedEvent)} eventId={selectedEventId} canManageStatus={session.permissions.includes('ticket.status')} />}
              {screen === 'manualValidation' && <ManualValidationView eventId={selectedEventId} hasEvent={Boolean(selectedEvent)} />}
              {screen === 'audit' && <AuditView attempts={attempts} loading={operationalLoading} />}
              {screen === 'configuration' && <ConfigurationView eventId={selectedEventId} />}
              {screen === 'messages' && <MessagesView eventId={selectedEventId} />}
              {screen === 'users' && <UsersView />}
              {screen === 'import' && <ImportView eventId={selectedEventId} />}
              {screen === 'importLogs' && <ImportLogsView eventId={selectedEventId} />}
              {screen === 'reports' && <ReportsView eventId={selectedEventId} gates={[]} sectors={[]} />}
              {screen === 'clients' && <ClientsView />}
            </>
          )}
        </div>
      </main></>
      )}
    </div>
  );
}

const DASHBOARD_REFRESH_OPTIONS = [10, 30, 60, 120] as const;

function Dashboard({ selectedEvent, onOpen }: { selectedEvent: EventView | null; onOpen: (screen: Screen) => void }) {
  const [report, setReport] = useState<import('./types').ValidationReport | null>(null);
  const [ticketSum, setTicketSum] = useState<TicketSummaryView | null>(null);
  const [loadingDash, setLoadingDash] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState<number>(30);
  const [countdown, setCountdown] = useState(refreshInterval);
  const [kiosk, setKiosk] = useState(false);
  const kioskRootRef = useRef<HTMLDivElement | null>(null);

  const load = async () => {
    if (!selectedEvent) return;
    setLoadingDash(true);
    try {
      const [rep, ts] = await Promise.all([
        api.getValidationReport(selectedEvent.id),
        api.getTicketSummary(selectedEvent.id),
      ]);
      setReport(rep);
      setTicketSum(ts);
    }
    catch { /* silencia — mostra último snapshot */ }
    finally { setLoadingDash(false); setCountdown(refreshInterval); }
  };

  // Carrega quando o evento muda
  useEffect(() => {
    setReport(null);
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedEvent?.id]);

  // Auto-refresh: decrementa o contador e recarrega ao chegar em 0.
  // Isso já cobre qualquer alteração feita durante o evento (nova portaria, nova
  // associação portaria x setor, novos lotes/tickets emitidos) — o próximo ciclo
  // busca os dados atuais do banco, sem cache.
  useEffect(() => {
    if (!selectedEvent) return;
    setCountdown(refreshInterval);
    const tick = setInterval(() => {
      setCountdown(c => {
        if (c <= 1) { load(); return refreshInterval; }
        return c - 1;
      });
    }, 1000);
    return () => clearInterval(tick);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedEvent?.id, refreshInterval]);

  // Modo kiosk: usa a Fullscreen API real do navegador (não só CSS), e volta ao
  // normal automaticamente se o usuário apertar Esc ou saltar pela API do navegador.
  useEffect(() => {
    function handleFullscreenChange() {
      if (!document.fullscreenElement) setKiosk(false);
    }
    document.addEventListener('fullscreenchange', handleFullscreenChange);
    return () => document.removeEventListener('fullscreenchange', handleFullscreenChange);
  }, []);

  async function toggleKiosk() {
    if (!kiosk) {
      try { await kioskRootRef.current?.requestFullscreen(); } catch { /* navegador pode negar — segue em modo visual */ }
      setKiosk(true);
    } else {
      if (document.fullscreenElement) { try { await document.exitFullscreen(); } catch { /* ignora */ } }
      setKiosk(false);
    }
  }

  if (!selectedEvent) return (
    <section className="panel full-panel">
      <EmptyState message="Selecione um evento para monitorar." />
    </section>
  );

  const s = report?.summary;
  // Usa o TicketSummaryView para contagens consistentes com a tela de Tickets
  const totalTickets  = ticketSum?.total   ?? s?.totalTickets  ?? 0;
  const activeTickets = ticketSum?.active  ?? s?.totalTickets  ?? 0;
  const validated     = s?.uniqueTickets ?? 0;  // entrou pelo menos 1 vez
  const pending       = Math.max(0, activeTickets - validated);
  const peopleInside  = ticketSum?.peopleInside ?? s?.peopleInside ?? 0;
  const coveragePct   = activeTickets > 0 ? Math.round(validated / activeTickets * 100) : 0;

  return <div ref={kioskRootRef} className={kiosk ? 'dash-kiosk-root' : undefined}>
    {/* Cabeçalho do evento */}
    <section className="dash-event-header">
      <div>
        <p className="panel-kicker">MONITORAMENTO AO VIVO</p>
        <h2 className="dash-event-name">{selectedEvent.name}</h2>
        <span className="dash-event-date">{formatDate(selectedEvent.startsAt, true)}</span>
      </div>
      <div className="dash-refresh-block">
        <button className="secondary-button" onClick={load} disabled={loadingDash}>
          {loadingDash ? <><span className="spinner" style={{ width: 11, height: 11 }} /></> : '↻'} Atualizar
        </button>
        <span className="dash-countdown">↻ {countdown}s</span>
        <select
          aria-label="Intervalo de atualização"
          className="dash-refresh-select"
          value={refreshInterval}
          onChange={(e) => setRefreshInterval(Number(e.target.value))}
        >
          {DASHBOARD_REFRESH_OPTIONS.map((secs) => (
            <option key={secs} value={secs}>{secs < 60 ? `${secs}s` : `${secs / 60}min`}</option>
          ))}
        </select>
        <button className="secondary-button" onClick={toggleKiosk}>{kiosk ? '✕ Sair do kiosk' : '⛶ Kiosk'}</button>
      </div>
    </section>

    {/* 4 métricas principais */}
    <section className="dash-metrics">
      <div className="dash-metric">
        <span className="dash-metric-label">Total de ingressos</span>
        <strong className="dash-metric-value">{totalTickets.toLocaleString('pt-BR')}</strong>
        {ticketSum && totalTickets !== activeTickets && <small>{activeTickets.toLocaleString('pt-BR')} ativos</small>}
      </div>
      <div className="dash-metric dash-metric-green">
        <span className="dash-metric-label">Validados</span>
        <strong className="dash-metric-value">{validated.toLocaleString('pt-BR')}</strong>
        <small>{coveragePct}%</small>
      </div>
      <div className={`dash-metric ${pending > 0 ? 'dash-metric-orange' : 'dash-metric-green'}`}>
        <span className="dash-metric-label">Faltam validar</span>
        <strong className="dash-metric-value">{pending.toLocaleString('pt-BR')}</strong>
      </div>
      <div className="dash-metric dash-metric-blue">
        <span className="dash-metric-label">Dentro agora</span>
        <strong className="dash-metric-value">{peopleInside.toLocaleString('pt-BR')}</strong>
        <small>pessoas</small>
      </div>
    </section>

    {/* Barra de cobertura geral */}
    <section className="panel dash-coverage-panel">
      <div className="dash-coverage-header">
        <span>Cobertura geral do evento</span>
        <strong>{validated.toLocaleString('pt-BR')} de {activeTickets.toLocaleString('pt-BR')} ativos</strong>
      </div>
      <div className="dash-bar-bg">
        <div className="dash-bar-fill" style={{ width: `${Math.min(coveragePct, 100)}%` }} />
      </div>
    </section>

    <div className="dash-grid">
      {/* Por portaria — taxa de aprovação das tentativas nesta portaria (não é cobertura
          de ingressos: uma portaria pode atender vários setores, então não existe um
          "total esperado" único por portaria como existe por setor). */}
      {report && report.byGate.length > 0 && (
        <section className="panel dash-sub-panel">
          <div className="panel-heading"><div><p className="panel-kicker">PORTARIAS</p><h2>Aprovação por portaria</h2>
            <p className="panel-subtitle" style={{ margin: '2px 0 0' }}>Tentativas aprovadas sobre o total de tentativas nesta portaria.</p></div></div>
          <div className="dash-rows">
            {report.byGate.map(g => {
              const pct = g.attempts > 0 ? Math.round(g.approved / g.attempts * 100) : 0;
              return (
                <div className="dash-row" key={g.gateId}>
                  <span className="dash-row-label">{g.gateName}</span>
                  <div className="dash-row-bar-wrap">
                    <div className="dash-bar-bg dash-bar-sm">
                      <div className="dash-bar-fill dash-bar-gate" style={{ width: `${Math.min(pct, 100)}%` }} />
                    </div>
                  </div>
                  <span className="dash-row-nums">
                    <strong>{g.approved.toLocaleString('pt-BR')}</strong>
                    <small>/{g.attempts.toLocaleString('pt-BR')} tentativas</small>
                    <em>{pct}%</em>
                  </span>
                </div>
              );
            })}
          </div>
        </section>
      )}

      {/* Por setor */}
      {report && report.bySector.length > 0 && (
        <section className="panel dash-sub-panel">
          <div className="panel-heading"><div><p className="panel-kicker">SETORES</p><h2>Por setor</h2></div></div>
          <div className="dash-rows">
            {report.bySector.map(sec => {
              const pct = sec.totalTickets > 0 ? Math.round(sec.validatedTickets / sec.totalTickets * 100) : 0;
              return (
                <div className="dash-row" key={sec.sectorId}>
                  <span className="dash-row-label">{sec.sectorName}</span>
                  <div className="dash-row-bar-wrap">
                    <div className="dash-bar-bg dash-bar-sm">
                      <div className="dash-bar-fill dash-bar-sector" style={{ width: `${Math.min(pct, 100)}%` }} />
                    </div>
                  </div>
                  <span className="dash-row-nums">
                    <strong>{sec.validatedTickets.toLocaleString('pt-BR')}</strong>
                    <small>/{sec.totalTickets.toLocaleString('pt-BR')}</small>
                    {sec.peopleInside > 0 && <em>{sec.peopleInside} dentro</em>}
                  </span>
                </div>
              );
            })}
          </div>
        </section>
      )}
    </div>

    {/* Últimos 20 acessos */}
    <section className="panel dash-feed-panel">
      <div className="panel-heading">
        <div><p className="panel-kicker">AO VIVO</p><h2>Últimos acessos</h2></div>
        <button className="text-button" onClick={() => onOpen('audit')}>Ver todos →</button>
      </div>
      {!report || report.recentAttempts.length === 0
        ? <EmptyState message="Nenhum acesso registrado ainda." />
        : <div className="dash-feed">
            {report.recentAttempts.slice(0, 20).map(a => (
              <div className={`dash-feed-row ${a.decision === 'Approved' ? 'feed-ok' : 'feed-nok'}`} key={a.attemptId}>
                <span className="feed-icon">{a.decision === 'Approved' ? '✓' : '✕'}</span>
                <span className="feed-time">{new Date(a.requestedAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</span>
                <span className="feed-gate">{a.gateName ?? '—'}</span>
                <span className="feed-dir">{a.direction === 'Entry' ? '↓' : '↑'}</span>
                <span className="feed-code">{a.ticketExternalId ?? a.credentialCode}{a.sectorName ? ` (${a.sectorName})` : ''}</span>
                {a.decision !== 'Approved' && a.reason && <span className="feed-reason">{a.reason}</span>}
              </div>
            ))}
          </div>
      }
    </section>
  </div>;
}

function EventsView({ events, onCreated }: { events: EventView[]; onCreated: (event: EventView) => void }) {
  const [showForm, setShowForm] = useState(false);
  const [venues, setVenues] = useState<VenueView[]>([]);
  const [clients, setClients] = useState<ClientView[]>([]);
  const [adminOpenId, setAdminOpenId] = useState<string | null>(null);
  const [loadingVenues, setLoadingVenues] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [venueName, setVenueName] = useState('');
  const [venueCity, setVenueCity] = useState('');
  const [venueState, setVenueState] = useState('');
  const [eventName, setEventName] = useState('');
  const [eventStatus, setEventStatus] = useState('Draft');
  const [organizer, setOrganizer] = useState('');
  const [clientId, setClientId] = useState('');
  const [startsAt, setStartsAt] = useState('');
  const [endsAt, setEndsAt] = useState('');

  useEffect(() => {
    if (!showForm) return;
    setLoadingVenues(true);
    Promise.all([api.listVenues(), api.listClients(true)])
      .then(([v, c]) => { setVenues(v); setClients(c); })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Não foi possível carregar dados.'))
      .finally(() => setLoadingVenues(false));
  }, [showForm]);

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!venueName.trim() || !eventName.trim() || !startsAt || !endsAt) {
      setError('Informe o local, nome, início e fim do evento.');
      return;
    }
    if (new Date(startsAt) >= new Date(endsAt)) {
      setError('O início do evento deve ser anterior ao fim.');
      return;
    }
    setSaving(true);
    setError(null);
    setMessage(null);
    try {
      const venue = await api.createVenue({
        name: venueName.trim(),
        city: venueCity.trim() || undefined,
        state: venueState.trim().toUpperCase() || undefined,
      });
      const created = await api.createEvent({
        venueId: venue.id,
        name: eventName.trim(),
        organizer: organizer.trim() || undefined,
        clientId: clientId || undefined,
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
        status: eventStatus,
      });
      setVenues((current) => [...current, venue]);
      onCreated(created);
      setMessage('Local e evento criados com sucesso.');
      setShowForm(false);
      setVenueName('');
      setVenueCity('');
      setVenueState('');
      setEventName('');
      setEventStatus('Draft');
      setOrganizer('');
      setClientId('');
      setStartsAt('');
      setEndsAt('');
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível criar o evento.');
    } finally {
      setSaving(false);
    }
  }

  return <>
    <section className="panel full-panel"><div className="panel-heading"><div><p className="panel-kicker">CATÁLOGO</p><h2>Eventos cadastrados</h2><p className="panel-subtitle">Acompanhe o calendário e crie novos eventos com seu local.</p></div><button className="primary-button" onClick={() => { setShowForm((current) => !current); setError(null); }}>{showForm ? 'Fechar cadastro' : '+ Novo evento'}</button></div>{message && <div className="alert-success inline-alert"><strong>{message}</strong></div>}{error && <div className="alert-error inline-alert"><strong>Não foi possível concluir.</strong><span>{error}</span></div>}{showForm && <form className="event-create-form" onSubmit={handleCreate}><div className="form-section-title">Local do evento</div><div className="form-row form-row-three"><label>Nome do local<input value={venueName} onChange={(event) => setVenueName(event.target.value)} placeholder="Ex.: Centro de Convenções" disabled={saving || loadingVenues} /></label><label>Cidade<input value={venueCity} onChange={(event) => setVenueCity(event.target.value)} placeholder="São Paulo" disabled={saving} /></label><label>UF<input maxLength={2} value={venueState} onChange={(event) => setVenueState(event.target.value)} placeholder="SP" disabled={saving} /></label></div><div className="form-section-title">Dados do evento</div><div className="form-row form-row-three"><label>Nome do evento<input value={eventName} onChange={(event) => setEventName(event.target.value)} placeholder="Ex.: Festival FastPass" disabled={saving} /></label><label>Cliente / Organizador<select value={clientId} onChange={(event) => setClientId(event.target.value)} disabled={saving || loadingVenues}><option value="">— Sem cliente vinculado —</option>{clients.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><label>Responsável interno<input value={organizer} onChange={(event) => setOrganizer(event.target.value)} placeholder="Nome do responsável" disabled={saving} /></label></div><div className="form-row form-row-three"><label>Status inicial<select value={eventStatus} onChange={(event) => setEventStatus(event.target.value)} disabled={saving}><option value="Draft">Rascunho</option><option value="Preparing">Preparando</option><option value="Published">Publicado</option><option value="Running">Em andamento</option><option value="Closed">Encerrado</option><option value="Archived">Arquivado</option></select></label><label>Início<input type="datetime-local" value={startsAt} onChange={(event) => setStartsAt(event.target.value)} disabled={saving} /></label><label>Fim<input type="datetime-local" value={endsAt} onChange={(event) => setEndsAt(event.target.value)} disabled={saving} /></label></div><div className="event-form-actions"><small>{loadingVenues ? 'Consultando locais existentes...' : 'O local informado será cadastrado junto com o evento.'}</small><button className="primary-button" type="submit" disabled={saving}>{saving ? 'Criando...' : 'Criar local e evento'}</button></div></form>}{events.length === 0 ? <EmptyState message="Nenhum evento cadastrado na API." /> : <div className="table-scroll"><table><thead><tr><th>Evento</th><th>Cliente</th><th>Responsável</th><th>Início</th><th>Fim</th><th>Status</th><th /></tr></thead><tbody>{events.map((event) => <Fragment key={event.id}>
      <tr>
        <td><strong>{event.name}</strong><small className="table-id">{event.id}</small></td>
        <td>{event.clientName || <span style={{ color: '#bcc3cf' }}>—</span>}</td>
        <td><small>{event.organizer || '—'}</small></td>
        <td>{formatDate(event.startsAt, true)}</td>
        <td>{formatDate(event.endsAt, true)}</td>
        <td><StatusBadge value={event.status} /></td>
        <td style={{ display: 'flex', gap: 4 }}>
          <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px' }}
            onClick={() => setAdminOpenId(adminOpenId === event.id ? null : event.id)}>
            {adminOpenId === event.id ? 'Fechar' : '⚙ Administrar'}
          </button>
          <button className="row-menu" title="Excluir evento" onClick={async () => { if (!window.confirm(`Excluir o evento "${event.name}"? Só é possível se não houver ingressos.`)) return; try { await (api as any).deleteEvent(event.id); window.location.reload(); } catch (r: any) { alert(r?.message ?? 'Erro'); } }}>🗑</button>
        </td>
      </tr>
      {adminOpenId === event.id && (
        <tr>
          <td colSpan={7} style={{ background: '#fafbfe', padding: 0 }}>
            <div style={{ padding: '18px 22px' }}>
              <p className="panel-kicker" style={{ marginBottom: 4 }}>ADMINISTRAÇÃO DO EVENTO</p>
              <EventAdminPanel eventId={event.id} eventName={event.name} />
            </div>
          </td>
        </tr>
      )}
    </Fragment>)}</tbody></table></div>}</section>
  </>;
}

/** Painel de administração de um evento — resetar log de acessos e excluir todos os dados.
 *  Renderizado inline (linha expansível) na tabela de Eventos. */
function EventAdminPanel({ eventId, eventName }: { eventId: string; eventName: string }) {
  const [tab, setTab] = useState<'reset' | 'delete'>('reset');
  const [summary, setSummary] = useState<import('./types').DataSummary | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    api.getDataSummary(eventId).then(setSummary).catch(() => {});
  }, [eventId]);

  async function handleReset() {
    if (!window.confirm('Zerar TODO o log de acessos e contadores de presença? Esta ação é irreversível.')) return;
    setSaving(true); setError(null); setNotice(null);
    try {
      const r = await api.resetAccessLog(eventId);
      setNotice(r.message);
      setSummary(await api.getDataSummary(eventId));
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro.'); }
    finally { setSaving(false); }
  }

  async function handleDeleteData() {
    setSaving(true); setError(null); setNotice(null);
    try {
      const r = await api.deleteEventData(eventId, deleteConfirm);
      setNotice(r.message);
      setDeleteConfirm('');
      setSummary(await api.getDataSummary(eventId));
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro.'); }
    finally { setSaving(false); }
  }

  return <div className="event-admin-panel">
    {summary && <div style={{ display: 'flex', gap: 10, marginBottom: 14 }}>
      <div className="import-stat"><strong>{summary.tickets.toLocaleString('pt-BR')}</strong><span>Ingressos</span></div>
      <div className="import-stat errored"><strong>{summary.attempts.toLocaleString('pt-BR')}</strong><span>Tentativas</span></div>
      <div className="import-stat skipped"><strong>{summary.imports.toLocaleString('pt-BR')}</strong><span>Importações</span></div>
    </div>}

    <div className="users-tabs" style={{ marginBottom: 12 }}>
      <button className={tab === 'reset' ? 'users-tab active' : 'users-tab'} onClick={() => setTab('reset')}>Resetar log de acessos</button>
      <button className={tab === 'delete' ? 'users-tab active' : 'users-tab'} onClick={() => setTab('delete')}>Excluir todos os dados</button>
    </div>
    {error && <div className="alert-error"><strong>{error}</strong></div>}
    {notice && <div className="alert-success"><strong>{notice}</strong></div>}

    {tab === 'reset' && <div>
      <p style={{ color: '#8d99ab', fontSize: 11, marginBottom: 12, maxWidth: 560 }}>
        Remove todo o histórico de tentativas e zera os contadores de entradas e presença dos ingressos.
        Use para simulações ou testes antes do evento real. <strong>Ação irreversível.</strong>
      </p>
      <button className="primary-button" style={{ background: '#e66c7d', boxShadow: '0 4px 10px #e66c7d33' }}
        disabled={saving} onClick={handleReset}>
        {saving ? 'Resetando...' : '⚠ Resetar log e zerar contadores'}
      </button>
    </div>}

    {tab === 'delete' && <div style={{ display: 'grid', gap: 14, maxWidth: 520 }}>
      <p style={{ color: '#8d99ab', fontSize: 11, margin: 0 }}>
        Remove permanentemente todos os ingressos, tentativas de acesso e importações do evento <strong>{eventName}</strong>.
        <strong> Esta ação não pode ser desfeita.</strong>
      </p>
      {summary && <div className="alert-error">
        <strong>Serão excluídos:</strong>
        <span>{summary.tickets.toLocaleString('pt-BR')} ingressos · {summary.attempts.toLocaleString('pt-BR')} tentativas · {summary.imports.toLocaleString('pt-BR')} importações</span>
      </div>}
      <label style={{ display: 'grid', gap: 8, color: '#718096', fontSize: 10, fontWeight: 700, letterSpacing: '.5px', textTransform: 'uppercase' }}>
        Para confirmar, digite exatamente:
        <code style={{ color: '#e66c7d', background: '#fff5f6', padding: '6px 10px', borderRadius: 6, fontSize: 11, userSelect: 'all' }}>
          EXCLUIR {eventId}
        </code>
        <input value={deleteConfirm} onChange={e => setDeleteConfirm(e.target.value)}
          placeholder="Cole ou digite o texto acima"
          style={{ padding: '10px 12px', border: '2px solid #ffd9df', borderRadius: 7, fontSize: 11, background: '#fff5f6', fontFamily: 'monospace' }} />
      </label>
      <button className="primary-button" style={{ background: '#c0392b', boxShadow: '0 4px 10px #c0392b33' }}
        disabled={saving || deleteConfirm !== `EXCLUIR ${eventId}`} onClick={handleDeleteData}>
        {saving ? 'Excluindo...' : '🗑 Excluir permanentemente'}
      </button>
    </div>}
  </div>;
}

function TicketsView({ tickets: _initialTickets, loading: _initialLoading, hasEvent, eventId, canManageStatus }: { tickets: TicketView[]; loading: boolean; hasEvent: boolean; eventId: string; canManageStatus: boolean }) {
  const [summary, setSummary] = useState<TicketSummaryView | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(false);
  const [tickets, setTickets] = useState<TicketView[]>(_initialTickets);
  const [ticketsLoading, setTicketsLoading] = useState(_initialLoading);
  const [searchCode, setSearchCode] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [statusChanging, setStatusChanging] = useState<string | null>(null);

  const loadSummary = () => {
    if (!eventId) { setSummary(null); return; }
    setSummaryLoading(true);
    api.getTicketSummary(eventId).then(setSummary).catch(() => {}).finally(() => setSummaryLoading(false));
  };

  const loadTickets = () => {
    if (!eventId) return;
    setTicketsLoading(true);
    const filters: { code?: string; status?: string } = {};
    if (searchCode.trim()) filters.code = searchCode.trim();
    if (filterStatus) filters.status = filterStatus;
    api.listTickets(eventId, filters).then(setTickets).catch(() => {}).finally(() => setTicketsLoading(false));
  };

  useEffect(() => { loadSummary(); }, [eventId]);
  useEffect(() => { setTickets(_initialTickets); }, [_initialTickets]);

  useEffect(() => {
    if (!eventId) return;
    const interval = setInterval(loadSummary, 15000);
    return () => clearInterval(interval);
  }, [eventId]);

  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    loadTickets();
  };

  const handleStatusChange = async (ticketId: string, newStatus: string) => {
    if (!window.confirm(`Alterar status do ticket para '${newStatus}'?`)) return;
    setStatusChanging(ticketId);
    try {
      await api.changeTicketStatus(eventId, ticketId, newStatus);
      loadTickets();
      loadSummary();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : String(err));
    } finally {
      setStatusChanging(null);
    }
  };

  if (!hasEvent) return <section className="panel full-panel"><EmptyState message="Selecione um evento para consultar os tickets." /></section>;
  return <>
    {summary && <section className="ticket-summary-bar">
      <div className="ticket-summary-item"><span>Total</span><strong>{summary.total.toLocaleString('pt-BR')}</strong></div>
      <div className="ticket-summary-item ts-active"><span>Ativos</span><strong>{summary.active.toLocaleString('pt-BR')}</strong></div>
      <div className="ticket-summary-item ts-used"><span>Usados</span><strong>{summary.used.toLocaleString('pt-BR')}</strong></div>
      <div className="ticket-summary-item ts-cancelled"><span>Cancelados</span><strong>{summary.cancelled.toLocaleString('pt-BR')}</strong></div>
      <div className="ticket-summary-item ts-revoked"><span>Revogados</span><strong>{summary.revoked.toLocaleString('pt-BR')}</strong></div>
      <div className="ticket-summary-item ts-inside"><span>Dentro agora</span><strong>{summary.peopleInside.toLocaleString('pt-BR')}</strong></div>
      <button className="secondary-button ticket-summary-refresh" onClick={loadSummary} disabled={summaryLoading} title="Atualizar agora">
        {summaryLoading ? <span className="spinner" style={{ width: 11, height: 11 }} /> : '↻'}
      </button>
    </section>}
    <section className="panel full-panel"><div className="panel-heading"><div><p className="panel-kicker">CREDENCIAIS DE ACESSO</p><h2>Tickets do evento</h2><p className="panel-subtitle">Busque por código, filtre por status e altere a situação dos ingressos.</p></div></div>
      <form className="ticket-filter-bar" onSubmit={handleSearch}>
        <div className="filter-field filter-search">
          <label>Código do ticket</label>
          <div className="filter-search-input">
            <span className="filter-search-icon">⌕</span>
            <input value={searchCode} onChange={(e) => setSearchCode(e.target.value)} placeholder="Digite o código exato" />
          </div>
        </div>
        <div className="filter-field">
          <label>Status</label>
          <select value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)}>
            <option value="">Todos</option>
            <option value="active">Ativo</option>
            <option value="cancelled">Cancelado</option>
            <option value="revoked">Revogado</option>
          </select>
        </div>
        <div className="filter-actions">
          <button className="primary-button" type="submit" disabled={ticketsLoading}>{ticketsLoading ? 'Buscando…' : 'Buscar'}</button>
          {(searchCode || filterStatus) && <button className="secondary-button" type="button" onClick={() => { setSearchCode(''); setFilterStatus(''); setTimeout(loadTickets, 0); }}>Limpar</button>}
        </div>
      </form>
      {ticketsLoading ? <div className="table-loading"><span className="spinner" />Carregando tickets...</div> : tickets.length === 0 ? <EmptyState message="Nenhum ticket encontrado com os filtros informados." /> : <div className="table-scroll"><table><thead><tr><th>Ticket</th><th>Código</th><th>Setor</th><th>Lote</th><th>Entradas</th><th>Quota</th><th>Status</th>{canManageStatus && <th>Ações</th>}</tr></thead><tbody>{tickets.slice(0, 200).map((ticket) => <tr key={ticket.id}><td><strong>{ticket.externalId || '—'}</strong><small className="table-id">{ticket.id.slice(0, 8)}</small></td><td><code>{ticket.code}</code></td><td>{ticket.sectorName || '—'}</td><td>{ticket.batchName || '—'}</td><td><div className="usage-cell"><span>{ticket.entriesUsed ?? 0}</span><div className="mini-progress"><i style={{ width: `${Math.min(((ticket.entriesUsed ?? 0) / Math.max(ticket.maximumEntries ?? 1, 1)) * 100, 100)}%` }} /></div></div></td><td>{ticket.maximumEntries ?? 1}</td><td><StatusBadge value={ticket.status} /></td>{canManageStatus && <td><select className="status-select" disabled={statusChanging === ticket.id} value={ticket.status} onChange={(e) => handleStatusChange(ticket.id, e.target.value)}><option value="active">Ativo</option><option value="cancelled">Cancelado</option><option value="revoked">Revogado</option></select></td>}</tr>)}</tbody></table></div>}
      {tickets.length > 200 && <p className="panel-subtitle" style={{ padding: '8px 16px' }}>Mostrando 200 de {tickets.length} tickets. Use os filtros para refinar.</p>}
    </section>
  </>;
}

function GateSectorMatrix({ gates, sectors, rules, savingKey, removingKey, modeSavingKey, onToggle, onRemoveGate, onRemoveSector, onSetMode }: { gates: GateView[]; sectors: SectorView[]; rules: GateSectorView[]; savingKey: string | null; removingKey: string | null; modeSavingKey: string | null; onToggle: (gateId: string, sectorId: string, direction: string, checked: boolean) => void; onRemoveGate: (gate: GateView) => void; onRemoveSector: (sector: SectorView) => void; onSetMode: (gate: GateView, operationMode: GateView['operationMode']) => void }) {
  return <div className="matrix-scroll"><table className="access-matrix"><thead><tr><th className="matrix-sector-heading">Setor / Portaria</th>{gates.map((gate) => <th key={gate.id} title={gate.code || gate.name}><span>{gate.name}</span>{gate.code && <small>{gate.code}</small>}<button type="button" className="matrix-remove-button" disabled={removingKey === `gate:${gate.id}`} onClick={() => onRemoveGate(gate)}>{removingKey === `gate:${gate.id}` ? 'Removendo...' : 'Remover'}</button><select aria-label={`Modo da ${gate.name}`} disabled={modeSavingKey === `mode:${gate.id}`} value={gate.operationMode} onChange={(event) => onSetMode(gate, event.target.value as GateView['operationMode'])}><option value="EntryAndExitValidated">Entrada e saída validadas</option><option value="EntryValidatedExitFree">Entrada validada, saída livre</option></select></th>)}</tr></thead><tbody>{sectors.map((sector) => <tr key={sector.id}><th><strong>{sector.name}</strong>{sector.capacity && <small>Capacidade {sector.capacity}</small>}<button type="button" className="matrix-remove-button sector-remove" disabled={removingKey === `sector:${sector.id}`} onClick={() => onRemoveSector(sector)}>{removingKey === `sector:${sector.id}` ? 'Removendo...' : 'Remover'}</button></th>{gates.map((gate) => { const entryRule = rules.find((rule) => rule.gateId === gate.id && rule.sectorId === sector.id && rule.direction === 'Entry'); const exitRule = rules.find((rule) => rule.gateId === gate.id && rule.sectorId === sector.id && rule.direction === 'Exit'); const entryKey = `${gate.id}:${sector.id}:Entry`; const exitKey = `${gate.id}:${sector.id}:Exit`; return <td key={gate.id}><div className="matrix-checkboxes"><label className="matrix-checkbox entry"><input type="checkbox" checked={Boolean(entryRule)} disabled={savingKey === entryKey || savingKey === exitKey} onChange={(event) => onToggle(gate.id, sector.id, 'Entry', event.target.checked)} /><span className="checkbox-box">✓</span><span>Entrada</span></label><label className="matrix-checkbox exit"><input type="checkbox" checked={Boolean(exitRule)} disabled={savingKey === entryKey || savingKey === exitKey} onChange={(event) => onToggle(gate.id, sector.id, 'Exit', event.target.checked)} /><span className="checkbox-box">✓</span><span>Saída</span></label></div></td>; })}</tr>)}</tbody></table></div>;
}

function DeviceRow({ device, saving, onSave }: { device: DeviceView; saving: boolean; onSave: (device: DeviceView, payload: { name: string; identifier?: string; deviceType: DeviceType; configurationJson?: string; active: boolean }) => void }) {
  const [name, setName] = useState(device.name);
  const [identifier, setIdentifier] = useState(device.identifier ?? '');
  const [deviceType, setDeviceType] = useState<DeviceType>(device.deviceType);
  const [configurationJson, setConfigurationJson] = useState(device.configurationJson ?? '');
  const [active, setActive] = useState(device.active);

  useEffect(() => {
    setName(device.name);
    setIdentifier(device.identifier ?? '');
    setDeviceType(device.deviceType);
    setConfigurationJson(device.configurationJson ?? '');
    setActive(device.active);
  }, [device]);

  return <tr>
    <td><input value={name} onChange={(event) => setName(event.target.value)} disabled={saving} /><small className="table-id">{device.id}</small></td>
    <td><input value={identifier} onChange={(event) => setIdentifier(event.target.value)} placeholder="Opcional" disabled={saving} /></td>
    <td><select value={deviceType} onChange={(event) => setDeviceType(event.target.value as DeviceType)} disabled={saving}><option value="Simulator">Simulator</option><option value="Serial">Serial</option><option value="Vcom">Vcom</option><option value="Mqtt">Mqtt</option><option value="Legacy">Legacy</option></select></td>
    <td><textarea rows={2} value={configurationJson} onChange={(event) => setConfigurationJson(event.target.value)} placeholder='{"port":"COM3"}' disabled={saving} /></td>
    <td>{device.lastSeenAt ? formatDate(device.lastSeenAt, true) : 'Nunca'}</td>
    <td><label className="matrix-checkbox"><input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span><span>{active ? 'Ativo' : 'Inativo'}</span></label></td>
    <td><button type="button" className="secondary-button" disabled={saving || !name.trim()} onClick={() => onSave(device, { name: name.trim(), identifier: identifier.trim() || undefined, deviceType, configurationJson: configurationJson.trim() || undefined, active })}>{saving ? 'Salvando...' : 'Salvar'}</button></td>
  </tr>;
}

type MessageFormPayload = { title: string; message: string; backgroundStart: string; backgroundEnd: string; titleColor: string; messageColor: string; titleSize: string; messageSize: string; titleBold: boolean; messageBold: boolean; active: boolean };

function MessageEditor({ item, saving, onSave, onRestore, restoring }: {
  item: AccessMessageView; saving: boolean;
  onSave: (code: string, payload: MessageFormPayload) => void;
  onRestore?: (code: string) => void; restoring?: boolean;
}) {
  const [title, setTitle] = useState(item.title);
  const [message, setMessage] = useState(item.message);
  const [backgroundStart, setBackgroundStart] = useState(item.backgroundStart);
  const [backgroundEnd, setBackgroundEnd] = useState(item.backgroundEnd);
  const [titleColor, setTitleColor] = useState(item.titleColor);
  const [messageColor, setMessageColor] = useState(item.messageColor);
  const [titleSize, setTitleSize] = useState(item.titleSize);
  const [messageSize, setMessageSize] = useState(item.messageSize);
  const [titleBold, setTitleBold] = useState(item.titleBold);
  const [messageBold, setMessageBold] = useState(item.messageBold);
  const [active, setActive] = useState(item.active);

  useEffect(() => {
    setTitle(item.title); setMessage(item.message); setBackgroundStart(item.backgroundStart); setBackgroundEnd(item.backgroundEnd);
    setTitleColor(item.titleColor); setMessageColor(item.messageColor); setTitleSize(item.titleSize); setMessageSize(item.messageSize);
    setTitleBold(item.titleBold); setMessageBold(item.messageBold); setActive(item.active);
  }, [item]);

  return <article className="message-card">
    <div className="message-card-heading">
      <div><code>{item.code}</code>
        <span className={item.isCustomized ? 'message-badge customized' : 'message-badge default'}>
          {item.isCustomized ? '✎ Customizada neste evento' : '● Usando padrão global'}
        </span>
      </div>
      <label className="form-checkbox"><input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>{active ? 'Ativa' : 'Inativa'}</label>
    </div>
    <div className="message-preview" style={{ background: `linear-gradient(135deg, ${backgroundStart}, ${backgroundEnd})`, color: messageColor }}><strong style={{ color: titleColor, fontSize: titleSize, fontWeight: titleBold ? 700 : 400 }}>{title}</strong><span style={{ fontSize: messageSize, fontWeight: messageBold ? 700 : 400 }}>{message}</span></div>
    <div className="message-form-grid"><label>Título<input value={title} maxLength={120} onChange={(event) => setTitle(event.target.value)} disabled={saving} /></label><label>Mensagem<textarea value={message} maxLength={500} onChange={(event) => setMessage(event.target.value)} disabled={saving} /></label><label>Cor início<input type="color" value={backgroundStart} onChange={(event) => setBackgroundStart(event.target.value)} disabled={saving} /></label><label>Cor fim<input type="color" value={backgroundEnd} onChange={(event) => setBackgroundEnd(event.target.value)} disabled={saving} /></label><label>Cor título<input type="color" value={titleColor} onChange={(event) => setTitleColor(event.target.value)} disabled={saving} /></label><label>Cor mensagem<input type="color" value={messageColor} onChange={(event) => setMessageColor(event.target.value)} disabled={saving} /></label><label>Tamanho título<input value={titleSize} placeholder="28dp" onChange={(event) => setTitleSize(event.target.value)} disabled={saving} /></label><label>Tamanho mensagem<input value={messageSize} placeholder="18dp" onChange={(event) => setMessageSize(event.target.value)} disabled={saving} /></label><label className="form-checkbox"><input type="checkbox" checked={titleBold} onChange={(event) => setTitleBold(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>Título em negrito</label><label className="form-checkbox"><input type="checkbox" checked={messageBold} onChange={(event) => setMessageBold(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>Mensagem em negrito</label></div>
    <div className="message-card-actions">
      <small>Use <code>{'{status}'}</code> ou <code>{'{situacao}'}</code> para texto dinâmico.</small>
      <div style={{ display: 'flex', gap: 8 }}>
        {item.isCustomized && onRestore && <button className="secondary-button" type="button" style={{ color: '#8d99ab' }} disabled={saving || restoring} onClick={() => onRestore(item.code)}>{restoring ? 'Restaurando...' : '↺ Restaurar padrão'}</button>}
        <button className="secondary-button" type="button" disabled={saving || !title.trim() || !message.trim()} onClick={() => onSave(item.code, { title: title.trim(), message: message.trim(), backgroundStart, backgroundEnd, titleColor, messageColor, titleSize: titleSize.trim(), messageSize: messageSize.trim(), titleBold, messageBold, active })}>{saving ? 'Salvando...' : item.isCustomized ? 'Salvar customização' : 'Customizar para este evento'}</button>
      </div>
    </div>
  </article>;
}

function MessageTemplateEditor({ item, saving, onSave }: { item: AccessMessageTemplateView; saving: boolean; onSave: (code: string, payload: MessageFormPayload) => void }) {
  const [title, setTitle] = useState(item.title);
  const [message, setMessage] = useState(item.message);
  const [backgroundStart, setBackgroundStart] = useState(item.backgroundStart);
  const [backgroundEnd, setBackgroundEnd] = useState(item.backgroundEnd);
  const [titleColor, setTitleColor] = useState(item.titleColor);
  const [messageColor, setMessageColor] = useState(item.messageColor);
  const [titleSize, setTitleSize] = useState(item.titleSize);
  const [messageSize, setMessageSize] = useState(item.messageSize);
  const [titleBold, setTitleBold] = useState(item.titleBold);
  const [messageBold, setMessageBold] = useState(item.messageBold);
  const [active, setActive] = useState(item.active);

  useEffect(() => {
    setTitle(item.title); setMessage(item.message); setBackgroundStart(item.backgroundStart); setBackgroundEnd(item.backgroundEnd);
    setTitleColor(item.titleColor); setMessageColor(item.messageColor); setTitleSize(item.titleSize); setMessageSize(item.messageSize);
    setTitleBold(item.titleBold); setMessageBold(item.messageBold); setActive(item.active);
  }, [item]);

  return <article className="message-card">
    <div className="message-card-heading"><div><code>{item.code}</code><span className="message-badge default">Padrão global · aplica-se a todos os eventos</span></div><label className="form-checkbox"><input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>{active ? 'Ativa' : 'Inativa'}</label></div>
    <div className="message-preview" style={{ background: `linear-gradient(135deg, ${backgroundStart}, ${backgroundEnd})`, color: messageColor }}><strong style={{ color: titleColor, fontSize: titleSize, fontWeight: titleBold ? 700 : 400 }}>{title}</strong><span style={{ fontSize: messageSize, fontWeight: messageBold ? 700 : 400 }}>{message}</span></div>
    <div className="message-form-grid"><label>Título<input value={title} maxLength={120} onChange={(event) => setTitle(event.target.value)} disabled={saving} /></label><label>Mensagem<textarea value={message} maxLength={500} onChange={(event) => setMessage(event.target.value)} disabled={saving} /></label><label>Cor início<input type="color" value={backgroundStart} onChange={(event) => setBackgroundStart(event.target.value)} disabled={saving} /></label><label>Cor fim<input type="color" value={backgroundEnd} onChange={(event) => setBackgroundEnd(event.target.value)} disabled={saving} /></label><label>Cor título<input type="color" value={titleColor} onChange={(event) => setTitleColor(event.target.value)} disabled={saving} /></label><label>Cor mensagem<input type="color" value={messageColor} onChange={(event) => setMessageColor(event.target.value)} disabled={saving} /></label><label>Tamanho título<input value={titleSize} placeholder="28dp" onChange={(event) => setTitleSize(event.target.value)} disabled={saving} /></label><label>Tamanho mensagem<input value={messageSize} placeholder="18dp" onChange={(event) => setMessageSize(event.target.value)} disabled={saving} /></label><label className="form-checkbox"><input type="checkbox" checked={titleBold} onChange={(event) => setTitleBold(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>Título em negrito</label><label className="form-checkbox"><input type="checkbox" checked={messageBold} onChange={(event) => setMessageBold(event.target.checked)} disabled={saving} /><span className="checkbox-box">✓</span>Mensagem em negrito</label></div>
    <div className="message-card-actions"><small>Use <code>{'{status}'}</code> ou <code>{'{situacao}'}</code> para texto dinâmico. Vale para todos os eventos, exceto os que tiverem customização própria.</small><button className="secondary-button" type="button" disabled={saving || !title.trim() || !message.trim()} onClick={() => onSave(item.code, { title: title.trim(), message: message.trim(), backgroundStart, backgroundEnd, titleColor, messageColor, titleSize: titleSize.trim(), messageSize: messageSize.trim(), titleBold, messageBold, active })}>{saving ? 'Salvando...' : 'Salvar padrão global'}</button></div>
  </article>;
}

function MessagesView({ eventId }: { eventId: string }) {
  const [tab, setTab] = useState<'event' | 'global'>('event');
  const [messages, setMessages] = useState<AccessMessageView[]>([]);
  const [templates, setTemplates] = useState<AccessMessageTemplateView[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadingTemplates, setLoadingTemplates] = useState(false);
  const [savingCode, setSavingCode] = useState<string | null>(null);
  const [restoringCode, setRestoringCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!eventId) { setMessages([]); return; }
    let cancelled = false;
    setLoading(true); setError(null); setNotice(null);
    api.listAccessMessages(eventId).then((result) => { if (!cancelled) setMessages(result); }).catch((reason: unknown) => { if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as mensagens.'); }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [eventId]);

  useEffect(() => {
    if (tab !== 'global') return;
    let cancelled = false;
    setLoadingTemplates(true); setError(null); setNotice(null);
    api.listMessageTemplates().then((result) => { if (!cancelled) setTemplates(result); }).catch((reason: unknown) => { if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os padrões globais.'); }).finally(() => { if (!cancelled) setLoadingTemplates(false); });
    return () => { cancelled = true; };
  }, [tab]);

  async function handleSaveEvent(code: string, payload: MessageFormPayload) {
    setSavingCode(code); setError(null); setNotice(null);
    try {
      const updated = await api.updateAccessMessage(eventId, code, payload);
      setMessages((current) => current.map((item) => item.code === updated.code ? updated : item));
      setNotice(`Mensagem ${updated.code} customizada para este evento.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a mensagem.');
    } finally { setSavingCode(null); }
  }

  async function handleRestore(code: string) {
    if (!window.confirm(`Remover a customização de "${code}" e voltar a usar o padrão global?`)) return;
    setRestoringCode(code); setError(null); setNotice(null);
    try {
      await api.restoreAccessMessageDefault(eventId, code);
      const refreshed = await api.listAccessMessages(eventId);
      setMessages(refreshed);
      setNotice(`Mensagem ${code} restaurada ao padrão global.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível restaurar.');
    } finally { setRestoringCode(null); }
  }

  async function handleSaveTemplate(code: string, payload: MessageFormPayload) {
    setSavingCode(code); setError(null); setNotice(null);
    try {
      const updated = await api.updateMessageTemplate(code, payload);
      setTemplates((current) => current.map((item) => item.code === updated.code ? updated : item));
      setNotice(`Padrão global "${updated.code}" atualizado — aplicado a todos os eventos sem customização própria.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar o padrão.');
    } finally { setSavingCode(null); }
  }

  const customizedCount = messages.filter(m => m.isCustomized).length;

  return <>
    <section className="panel config-intro"><div><p className="panel-kicker">ADMINISTRAÇÃO</p><h2>Mensagens de validação</h2><p className="panel-subtitle">O padrão vale para todos os eventos automaticamente. Customize apenas quando um evento específico precisar de um texto diferente.</p></div></section>

    <div className="users-tabs">
      <button className={tab === 'event' ? 'users-tab active' : 'users-tab'} onClick={() => setTab('event')}>
        Este evento {messages.length > 0 && <span className="users-tab-count">{customizedCount} customizada(s)</span>}
      </button>
      <button className={tab === 'global' ? 'users-tab active' : 'users-tab'} onClick={() => setTab('global')}>
        Padrões globais <span className="users-tab-count">{templates.length || 14}</span>
      </button>
    </div>
    {error && <div className="alert-error"><strong>Não foi possível concluir.</strong><span>{error}</span></div>}
    {notice && <div className="alert-success"><strong>{notice}</strong></div>}

    {tab === 'event' && (
      !eventId ? <section className="panel full-panel"><EmptyState message="Selecione um evento para ver suas mensagens." /></section>
      : loading ? <section className="panel full-panel"><div className="table-loading"><span className="spinner" />Carregando mensagens...</div></section>
      : <div className="messages-list">{messages.map((item) => <MessageEditor key={item.code} item={item} saving={savingCode === item.code} onSave={handleSaveEvent} onRestore={handleRestore} restoring={restoringCode === item.code} />)}</div>
    )}

    {tab === 'global' && (
      loadingTemplates ? <section className="panel full-panel"><div className="table-loading"><span className="spinner" />Carregando padrões...</div></section>
      : <div className="messages-list">{templates.map((item) => <MessageTemplateEditor key={item.code} item={item} saving={savingCode === item.code} onSave={handleSaveTemplate} />)}</div>
    )}
  </>;
}

function ConfigurationView({ eventId }: { eventId: string }) {
  const [gates, setGates] = useState<GateView[]>([]);
  const [devices, setDevices] = useState<DeviceView[]>([]);
  const [selectedGateId, setSelectedGateId] = useState('');
  const [sectors, setSectors] = useState<SectorView[]>([]);
  const [rules, setRules] = useState<GateSectorView[]>([]);
  const [cellSavingKey, setCellSavingKey] = useState<string | null>(null);
  const [removingKey, setRemovingKey] = useState<string | null>(null);
  const [modeSavingKey, setModeSavingKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gateName, setGateName] = useState('');
  const [gateCode, setGateCode] = useState('');
  const [sectorName, setSectorName] = useState('');
  const [sectorCapacity, setSectorCapacity] = useState('');
  const [catalogSaving, setCatalogSaving] = useState(false);
  const [deviceSavingKey, setDeviceSavingKey] = useState<string | null>(null);
  const [deviceName, setDeviceName] = useState('');
  const [deviceIdentifier, setDeviceIdentifier] = useState('');
  const [deviceType, setDeviceType] = useState<DeviceType>('Simulator');
  const [deviceConfiguration, setDeviceConfiguration] = useState('');

  useEffect(() => {
    if (!eventId) {
      setGates([]);
      setDevices([]);
      setSelectedGateId('');
      setSectors([]);
      setRules([]);
      setMessage(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    setMessage(null);
    setGates([]);
    setDevices([]);
    setSelectedGateId('');
    setSectors([]);
    setRules([]);
    Promise.all([api.listGates(eventId), api.listSectors(eventId), api.listGateSectors(eventId), api.listDevices(eventId)])
      .then(([gateResult, sectorResult, ruleResult, deviceResult]) => {
        if (cancelled) return;
        setGates(gateResult);
        setSelectedGateId((current) => current && gateResult.some((gate) => gate.id === current) ? current : gateResult[0]?.id ?? '');
        setDevices(deviceResult);
        setSectors(sectorResult);
        setRules(ruleResult);
      })
      .catch((reason: unknown) => {
        if (!cancelled) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a configuração.');
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [eventId]);

  async function handleCreateGate(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!gateName.trim()) {
      setError('Informe o nome da portaria.');
      return;
    }
    setCatalogSaving(true);
    setError(null);
    setMessage(null);
    try {
      const created = await api.createGate(eventId, { name: gateName.trim(), code: gateCode.trim() || undefined });
      setGates((current) => [...current, created].sort((left, right) => left.name.localeCompare(right.name)));
      setSelectedGateId(created.id);
      setGateName('');
      setGateCode('');
      setMessage(`Portaria “${created.name}” disponível no catálogo e selecionada.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível criar a portaria.');
    } finally {
      setCatalogSaving(false);
    }
  }

  async function handleCreateSector(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!sectorName.trim()) {
      setError('Informe o nome do setor.');
      return;
    }
    const capacity = sectorCapacity ? Number(sectorCapacity) : undefined;
    if (capacity !== undefined && (!Number.isInteger(capacity) || capacity < 1)) {
      setError('A capacidade do setor deve ser um número inteiro maior que zero.');
      return;
    }
    setCatalogSaving(true);
    setError(null);
    setMessage(null);
    try {
      const created = await api.createSector(eventId, { name: sectorName.trim(), capacity });
      setSectors((current) => [...current, created].sort((left, right) => left.name.localeCompare(right.name)));
      setSectorName('');
      setSectorCapacity('');
      setMessage(`Setor “${created.name}” disponível no catálogo.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível criar o setor.');
    } finally {
      setCatalogSaving(false);
    }
  }

  async function handleCreateDevice(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (!selectedGateId) {
      setError('Crie ou selecione uma portaria para cadastrar o dispositivo.');
      return;
    }
    if (!deviceName.trim()) {
      setError('Informe o nome do dispositivo.');
      return;
    }
    setDeviceSavingKey('new');
    setError(null);
    setMessage(null);
    try {
      const created = await api.createDevice(eventId, selectedGateId, {
        name: deviceName.trim(),
        identifier: deviceIdentifier.trim() || undefined,
        deviceType,
        configurationJson: deviceConfiguration.trim() || undefined,
      });
      setDevices((current) => [...current, created]);
      setDeviceName('');
      setDeviceIdentifier('');
      setDeviceType('Simulator');
      setDeviceConfiguration('');
      setMessage(`Dispositivo “${created.name}” cadastrado na portaria.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível cadastrar o dispositivo.');
    } finally {
      setDeviceSavingKey(null);
    }
  }

  async function handleUpdateDevice(device: DeviceView, payload: { name: string; identifier?: string; deviceType: DeviceType; configurationJson?: string; active: boolean }) {
    setDeviceSavingKey(device.id);
    setError(null);
    setMessage(null);
    try {
      const updated = await api.updateDevice(eventId, device.gateId, device.id, payload);
      setDevices((current) => current.map((item) => item.id === updated.id ? updated : item));
      setMessage(`Dispositivo “${updated.name}” atualizado.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível atualizar o dispositivo.');
    } finally {
      setDeviceSavingKey(null);
    }
  }

  async function handleRemoveGate(gate: GateView) {
    if (!window.confirm(`Remover a portaria “${gate.name}” deste evento? Ela só poderá ser removida se não tiver associações ativas na matriz.`)) return;
    const key = `gate:${gate.id}`;
    setRemovingKey(key);
    setError(null);
    setMessage(null);
    try {
      await api.removeGate(eventId, gate.id);
      const remainingGates = gates.filter((item) => item.id !== gate.id);
      setGates(remainingGates);
      setDevices((current) => current.filter((item) => item.gateId !== gate.id));
      if (selectedGateId === gate.id) setSelectedGateId(remainingGates[0]?.id ?? '');
      setRules((current) => current.filter((rule) => rule.gateId !== gate.id));
      setMessage(`Portaria “${gate.name}” removida do evento.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível remover a portaria.');
    } finally {
      setRemovingKey(null);
    }
  }

  async function handleRemoveSector(sector: SectorView) {
    if (!window.confirm(`Remover o setor “${sector.name}” deste evento? Ele só poderá ser removido se não tiver associações ativas na matriz.`)) return;
    const key = `sector:${sector.id}`;
    setRemovingKey(key);
    setError(null);
    setMessage(null);
    try {
      await api.removeSector(eventId, sector.id);
      setSectors((current) => current.filter((item) => item.id !== sector.id));
      setRules((current) => current.filter((rule) => rule.sectorId !== sector.id));
      setMessage(`Setor “${sector.name}” removido do evento.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível remover o setor.');
    } finally {
      setRemovingKey(null);
    }
  }

  async function handleSetMode(gate: GateView, operationMode: GateView['operationMode']) {
    if (gate.operationMode === operationMode) return;
    const key = `mode:${gate.id}`;
    setModeSavingKey(key);
    setError(null);
    setMessage(null);
    try {
      const updated = await api.setGateOperationMode(eventId, gate.id, operationMode);
      setGates((current) => current.map((item) => item.id === updated.id ? updated : item));
      setMessage(`Modo da portaria “${gate.name}” atualizado.`);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível atualizar o modo da portaria.');
    } finally {
      setModeSavingKey(null);
    }
  }

  async function handleToggleMatrix(gateIdToChange: string, sectorIdToChange: string, directionToChange: string, checked: boolean) {
    const existing = rules.find((rule) => rule.gateId === gateIdToChange && rule.sectorId === sectorIdToChange && rule.direction === directionToChange);
    const key = `${gateIdToChange}:${sectorIdToChange}:${directionToChange}`;
    if (!checked && !existing) return;
    if (!checked && !window.confirm(`Remover a permissão de ${directionToChange === 'Entry' ? 'entrada' : 'saída'} desta célula?`)) return;
    setCellSavingKey(key);
    setError(null);
    setMessage(null);
    try {
      if (checked && !existing) {
        const created = await api.createGateSector(eventId, { gateId: gateIdToChange, sectorId: sectorIdToChange, direction: directionToChange });
        setRules((current) => [...current, created]);
        setMessage(`${directionToChange === 'Entry' ? 'Entrada' : 'Saída'} liberada na matriz.`);
      } else if (!checked && existing) {
        await api.removeGateSector(eventId, existing.id);
        setRules((current) => current.filter((rule) => rule.id !== existing.id));
        setMessage(`${directionToChange === 'Entry' ? 'Entrada' : 'Saída'} removida da matriz.`);
      }
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível atualizar a matriz.');
    } finally {
      setCellSavingKey(null);
    }
  }

  if (!eventId) return <section className="panel full-panel"><EmptyState message="Selecione um evento para configurar suas portarias e setores." /></section>;

  return <>
    <section className="panel config-intro"><div><p className="panel-kicker">TOPOLOGIA DE ACESSO</p><h2>Portarias × setores</h2><p className="panel-subtitle">Defina por qual portaria cada setor pode ser acessado em cada direção.</p></div><div className="config-counts"><strong>{gates.length}</strong><span>portarias</span><strong>{sectors.length}</strong><span>setores</span></div></section>
    {error && <div className="alert-error"><strong>Não foi possível atualizar a configuração.</strong><span>{error}</span></div>}
    {message && <div className="alert-success"><strong>{message}</strong></div>}
    <section className="config-catalog-grid">
      <article className="panel config-mini-panel">
        <div className="panel-heading"><div><p className="panel-kicker">CATÁLOGO DE PORTARIAS</p><h2>Cadastrar portaria</h2><p className="panel-subtitle">As portarias já cadastradas aparecem abaixo.</p></div><span className="catalog-count">{gates.length}</span></div>
        <form className="config-form config-mini-form" onSubmit={handleCreateGate}><label>Nome<input value={gateName} onChange={(event) => setGateName(event.target.value)} placeholder="Ex.: Portaria Norte" disabled={catalogSaving} /></label><label>Código opcional<input value={gateCode} onChange={(event) => setGateCode(event.target.value)} placeholder="Ex.: PN-01" disabled={catalogSaving} /></label><button className="secondary-button form-submit" type="submit" disabled={catalogSaving}>{catalogSaving ? 'Salvando...' : 'Cadastrar portaria'}</button></form>
        <div className="catalog-items" aria-label="Portarias cadastradas">{gates.length === 0 ? <p className="catalog-empty">Nenhuma portaria cadastrada.</p> : gates.map((gate) => <div className={selectedGateId === gate.id ? 'catalog-item selected' : 'catalog-item'} key={gate.id}><button type="button" className="catalog-item-main" onClick={() => setSelectedGateId(gate.id)}><span><strong>{gate.name}</strong><small>{gate.code || 'Sem código'}</small></span>{selectedGateId === gate.id && <em>Selecionada</em>}</button><button type="button" className="catalog-remove" disabled={removingKey === `gate:${gate.id}`} onClick={() => handleRemoveGate(gate)}>{removingKey === `gate:${gate.id}` ? 'Removendo...' : 'Remover'}</button></div>)}</div>
      </article>
      <article className="panel config-mini-panel">
        <div className="panel-heading"><div><p className="panel-kicker">CATÁLOGO DE SETORES</p><h2>Cadastrar setor</h2><p className="panel-subtitle">Os setores já cadastrados aparecem abaixo.</p></div><span className="catalog-count">{sectors.length}</span></div>
        <form className="config-form config-mini-form" onSubmit={handleCreateSector}><label>Nome<input value={sectorName} onChange={(event) => setSectorName(event.target.value)} placeholder="Ex.: Pista premium" disabled={catalogSaving} /></label><label>Capacidade opcional<input type="number" min="1" value={sectorCapacity} onChange={(event) => setSectorCapacity(event.target.value)} placeholder="Ex.: 500" disabled={catalogSaving} /></label><button className="secondary-button form-submit" type="submit" disabled={catalogSaving}>{catalogSaving ? 'Salvando...' : 'Cadastrar setor'}</button></form>
        <div className="catalog-items" aria-label="Setores cadastrados">{sectors.length === 0 ? <p className="catalog-empty">Nenhum setor cadastrado.</p> : sectors.map((sector) => <div className="catalog-item" key={sector.id}><div className="catalog-item-main"><span><strong>{sector.name}</strong><small>{sector.capacity ? `Capacidade ${sector.capacity}` : 'Capacidade não informada'}</small></span></div><button type="button" className="catalog-remove" disabled={removingKey === `sector:${sector.id}`} onClick={() => handleRemoveSector(sector)}>{removingKey === `sector:${sector.id}` ? 'Removendo...' : 'Remover'}</button></div>)}</div>
      </article>
    </section>
    <section className="panel config-devices-panel"><div className="panel-heading"><div><p className="panel-kicker">DISPOSITIVOS</p><h2>Dispositivos das portarias</h2><p className="panel-subtitle">Cadastre simuladores agora e configure os dispositivos físicos quando estiverem disponíveis. Nenhuma conexão de hardware é iniciada por esta tela.</p></div><div className="config-counts"><strong>{devices.length}</strong><span>dispositivos</span></div></div><form className="config-form" onSubmit={handleCreateDevice}><label>Portaria<select value={selectedGateId} onChange={(event) => setSelectedGateId(event.target.value)} disabled={deviceSavingKey === 'new' || gates.length === 0}><option value="">Selecione</option>{gates.map((gate) => <option key={gate.id} value={gate.id}>{gate.name}{gate.code ? ` · ${gate.code}` : ''}</option>)}</select></label><label>Nome<input value={deviceName} onChange={(event) => setDeviceName(event.target.value)} placeholder="Ex.: Catraca Norte 01" disabled={deviceSavingKey === 'new'} /></label><label>Identifier opcional<input value={deviceIdentifier} onChange={(event) => setDeviceIdentifier(event.target.value)} placeholder="Ex.: catraca-norte-01" disabled={deviceSavingKey === 'new'} /></label><label>Tipo<select value={deviceType} onChange={(event) => setDeviceType(event.target.value as DeviceType)} disabled={deviceSavingKey === 'new'}><option value="Simulator">Simulator</option><option value="Serial">Serial</option><option value="Vcom">Vcom</option><option value="Mqtt">Mqtt</option><option value="Legacy">Legacy</option></select></label><label>Configuration JSON opcional<textarea rows={2} value={deviceConfiguration} onChange={(event) => setDeviceConfiguration(event.target.value)} placeholder='{"port":"COM3"}' disabled={deviceSavingKey === 'new'} /></label><button className="secondary-button form-submit" type="submit" disabled={deviceSavingKey === 'new' || !selectedGateId}>{deviceSavingKey === 'new' ? 'Cadastrando...' : 'Cadastrar dispositivo'}</button></form>{gates.length === 0 ? <EmptyState message="Crie uma portaria antes de cadastrar dispositivos." /> : devices.length === 0 ? <EmptyState message="Nenhum dispositivo cadastrado neste evento." /> : <div className="table-scroll"><table><thead><tr><th>Dispositivo</th><th>Identifier</th><th>Tipo</th><th>Configuration JSON</th><th>Último contato</th><th>Estado</th><th /></tr></thead><tbody>{devices.filter((device) => device.gateId === selectedGateId).map((device) => <DeviceRow key={device.id} device={device} saving={deviceSavingKey === device.id} onSave={handleUpdateDevice} />)}</tbody></table></div>}</section>
    <section className="panel config-rules-panel matrix-only-panel"><div className="panel-heading"><div><p className="panel-kicker">MATRIZ DE ACESSO</p><h2>Portarias × setores</h2><p className="panel-subtitle">Marque Entrada e/ou Saída diretamente em cada célula. Use “Remover” no cabeçalho para excluir uma portaria ou setor sem associações ativas.</p></div><div className="matrix-legend"><span><i className="entry-mark">↓</i>Entrada</span><span><i className="exit-mark">↑</i>Saída</span></div></div>{loading ? <div className="table-loading"><span className="spinner" />Carregando configuração...</div> : gates.length === 0 || sectors.length === 0 ? <EmptyState message="Crie ao menos uma portaria e um setor para visualizar a matriz." /> : <GateSectorMatrix gates={gates} sectors={sectors} rules={rules} savingKey={cellSavingKey} removingKey={removingKey} modeSavingKey={modeSavingKey} onToggle={handleToggleMatrix} onRemoveGate={handleRemoveGate} onRemoveSector={handleRemoveSector} onSetMode={handleSetMode} />}</section>
  </>;
}

function AuditView({ attempts, loading }: { attempts: AttemptPage | null; loading: boolean }) {
  return <section className="panel full-panel"><div className="panel-heading"><div><p className="panel-kicker">RASTREABILIDADE</p><h2>Auditoria de acessos</h2><p className="panel-subtitle">{attempts ? `${attempts.total} tentativa(s) encontrada(s)` : 'Tentativas registradas para o evento selecionado.'}</p></div><button className="secondary-button">Exportar relatório</button></div>{loading ? <div className="table-loading"><span className="spinner" />Carregando auditoria...</div> : <AttemptTable attempts={attempts?.data ?? []} expanded />}</section>;
}

function AttemptTable({ attempts, expanded = false }: { attempts: AttemptPage['data']; expanded?: boolean }) {
  if (attempts.length === 0) return <EmptyState message="Ainda não há tentativas de acesso registradas." />;
  return <div className="table-scroll"><table><thead><tr><th>Horário</th><th>Credencial</th><th>Portaria / setor</th><th>Setor do ingresso</th><th>Direção</th><th>Decisão</th><th>{expanded ? 'Motivo' : 'Status'}</th></tr></thead><tbody>{attempts.map((attempt) => <tr key={attempt.attemptId}><td>{formatDate(attempt.requestedAt || attempt.createdAt, true)}</td><td><strong>{attempt.credentialType === 'StaffBadge' ? attempt.staffName || 'Crachá de usuário' : attempt.ticketExternalId || 'Ingresso'}</strong>{attempt.channel === 'Manual' && <span className="message-badge customized" style={{ marginLeft: 6, fontSize: 9 }}>✋ Manual</span>}<small className="table-id">{attempt.credentialCodeMasked}</small></td><td><strong>{attempt.gateName || 'Portaria não informada'}</strong><small>{attempt.sectorName || 'Setor não informado'}</small></td><td>{attempt.ticketSectorName || '—'}</td><td><span className="direction">{attempt.direction === 'Entry' ? '↓ Entrada' : '↑ Saída'}</span></td><td><StatusBadge value={attempt.decision} /></td><td>{expanded ? attempt.reason || '—' : <span className="muted-text">{attempt.status}</span>}</td></tr>)}</tbody></table></div>;
}

interface ManualHistoryItem {
  key: string;
  code: string;
  approved: boolean;
  reason?: string | null;
  direction: string;
  credentialType: string;
  gateName: string;
  sectorName?: string | null;
  at: Date;
}

const AUTO_CLEAR_OPTIONS = [3, 5, 8, 0] as const; // 0 = não limpar automaticamente

function ManualValidationView({ eventId, hasEvent }: { eventId: string; hasEvent: boolean }) {
  const [gates, setGates] = useState<GateView[]>([]);
  const [sectors, setSectors] = useState<SectorView[]>([]);
  const [gateSectors, setGateSectors] = useState<GateSectorView[]>([]);
  const [loading, setLoading] = useState(true);
  const [gateId, setGateId] = useState('');
  const [sectorId, setSectorId] = useState('');
  const [code, setCode] = useState('');
  const [validating, setValidating] = useState(false);
  const [result, setResult] = useState<import('./types').AccessValidationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [autoClearSecs, setAutoClearSecs] = useState<number>(5);
  const [history, setHistory] = useState<ManualHistoryItem[]>([]);
  const codeInputRef = useRef<HTMLInputElement | null>(null);
  const clearTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!eventId) { setLoading(false); return; }
    setLoading(true);
    Promise.all([api.listGates(eventId), api.listSectors(eventId), api.listGateSectors(eventId)])
      .then(([g, s, gs]) => { setGates(g); setSectors(s); setGateSectors(gs); })
      .catch((r: unknown) => setError(r instanceof Error ? r.message : 'Erro ao carregar portarias/setores.'))
      .finally(() => setLoading(false));
  }, [eventId]);

  // Limpa o timer ao desmontar
  useEffect(() => () => { if (clearTimer.current) clearTimeout(clearTimer.current); }, []);

  const focusCode = () => { setTimeout(() => codeInputRef.current?.focus(), 50); };

  const clearResult = () => {
    if (clearTimer.current) { clearTimeout(clearTimer.current); clearTimer.current = null; }
    setResult(null);
    focusCode();
  };

  // Setores disponíveis para a portaria escolhida (via matriz)
  const sectorsForGate = gateId
    ? sectors.filter(s => gateSectors.some(gs => gs.gateId === gateId && gs.sectorId === s.id && gs.active))
    : [];

  const gateName = gates.find(g => g.id === gateId)?.name ?? '';

  async function handleValidate(e: FormEvent) {
    e.preventDefault();
    if (!code.trim() || !gateId) { setError('Informe o código e a portaria.'); return; }
    setValidating(true); setError(null); setResult(null);
    if (clearTimer.current) { clearTimeout(clearTimer.current); clearTimer.current = null; }
    const scannedCode = code.trim();
    try {
      const r = await api.validateManual({
        credentialCode: scannedCode,
        eventId,
        gateId,
        sectorId: sectorId || null,
        idempotencyKey: crypto.randomUUID(),
      });
      setResult(r);
      setHistory(prev => [{
        key: r.attemptId ?? crypto.randomUUID(),
        code: scannedCode,
        approved: r.approved,
        reason: r.reason,
        direction: r.direction,
        credentialType: r.credentialType,
        gateName,
        sectorName: sectors.find(s => s.id === sectorId)?.name ?? null,
        at: new Date(),
      }, ...prev].slice(0, 10));
      setCode('');
      focusCode();
      // Auto-limpa o resultado após o tempo configurado (0 = manter)
      if (autoClearSecs > 0) {
        clearTimer.current = setTimeout(() => { setResult(null); clearTimer.current = null; }, autoClearSecs * 1000);
      }
    } catch (r: unknown) {
      setError(r instanceof Error ? r.message : 'Erro na validação.');
      focusCode();
    } finally {
      setValidating(false);
    }
  }

  if (!hasEvent) return <section className="panel full-panel"><EmptyState message="Selecione um evento para validar manualmente." /></section>;
  if (loading) return <section className="panel full-panel"><div className="loading-panel"><span className="spinner" />Carregando portarias...</div></section>;

  return <>
    <section className="panel full-panel">
      <div className="panel-heading">
        <div><p className="panel-kicker">OPERAÇÃO DE PORTARIA</p><h2>Validação Manual</h2><p className="panel-subtitle">Use para liberar acesso em exceções (backstage, convidados, falha de catraca). Registrado como validação manual e contabilizado na portaria/setor escolhidos.</p></div>
        <label className="manual-autoclear">Limpar após
          <select value={autoClearSecs} onChange={(e) => setAutoClearSecs(Number(e.target.value))}>
            {AUTO_CLEAR_OPTIONS.map(s => <option key={s} value={s}>{s === 0 ? 'Manual' : `${s}s`}</option>)}
          </select>
        </label>
      </div>
      {error && <div className="alert-error inline-alert"><strong>Não foi possível validar.</strong><span>{error}</span></div>}
      <form className="ticket-filter-bar" onSubmit={handleValidate}>
        <div className="filter-field">
          <label>Portaria</label>
          <select value={gateId} onChange={(e) => { setGateId(e.target.value); setSectorId(''); }} disabled={validating}>
            <option value="">Selecione a portaria</option>
            {gates.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}
          </select>
        </div>
        <div className="filter-field">
          <label>Setor (opcional)</label>
          <select value={sectorId} onChange={(e) => setSectorId(e.target.value)} disabled={validating || !gateId}>
            <option value="">— Automático (setor do ingresso) —</option>
            {sectorsForGate.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </div>
        <div className="filter-field filter-search">
          <label>Código do ingresso / crachá</label>
          <div className="filter-search-input">
            <span className="filter-search-icon">⌕</span>
            <input ref={codeInputRef} value={code} onChange={(e) => setCode(e.target.value)} placeholder="Escaneie ou digite o código" disabled={validating} autoFocus />
          </div>
        </div>
        <div className="filter-actions">
          <button className="primary-button" type="submit" disabled={validating || !gateId || !code.trim()}>
            {validating ? 'Validando…' : '✋ Validar'}
          </button>
        </div>
      </form>
      {result && (
        <div className={`manual-result ${result.approved ? 'approved' : 'rejected'}`} onClick={clearResult} title="Clique para limpar">
          <button type="button" className="manual-result-close" onClick={(e) => { e.stopPropagation(); clearResult(); }}>✕</button>
          <strong>{result.approved ? '✓ ACESSO LIBERADO' : '✕ ACESSO NEGADO'}</strong>
          <span>{result.reason || (result.approved ? 'Validação manual registrada.' : '')}</span>
          <small>{result.direction === 'Entry' ? 'Entrada' : 'Saída'} · {result.credentialType === 'StaffBadge' ? 'Crachá de usuário' : 'Ingresso'} · Validação manual</small>
        </div>
      )}
    </section>

    <section className="panel full-panel">
      <div className="panel-heading"><div><p className="panel-kicker">HISTÓRICO</p><h2>Últimas validações manuais</h2><p className="panel-subtitle">Registros desta sessão (as validações também constam na Auditoria).</p></div>{history.length > 0 && <button className="secondary-button" onClick={() => setHistory([])}>Limpar histórico</button>}</div>
      {history.length === 0
        ? <EmptyState message="Nenhuma validação manual nesta sessão ainda." />
        : <div className="table-scroll"><table><thead><tr><th>Horário</th><th>Código</th><th>Portaria / setor</th><th>Direção</th><th>Resultado</th><th>Motivo</th></tr></thead>
            <tbody>{history.map(h => (
              <tr key={h.key}>
                <td>{h.at.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</td>
                <td><code>{h.code}</code></td>
                <td><strong>{h.gateName || '—'}</strong><small>{h.sectorName || 'Automático'}</small></td>
                <td><span className="direction">{h.direction === 'Entry' ? '↓ Entrada' : '↑ Saída'}</span></td>
                <td><StatusBadge value={h.approved ? 'Approved' : 'Rejected'} /></td>
                <td>{h.approved ? '—' : (h.reason || '—')}</td>
              </tr>
            ))}</tbody></table></div>}
    </section>
  </>;
}

function ClientsView() {
  const [clients, setClients] = useState<ClientView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editing, setEditing] = useState<ClientView | null>(null);
  const [name, setName] = useState('');
  const [document, setDocument] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setLoading(true);
    api.listClients(false).then(setClients).catch((r: unknown) => setError(r instanceof Error ? r.message : 'Erro')).finally(() => setLoading(false));
  }, []);

  function startEdit(c: ClientView) {
    setEditing(c); setName(c.name); setDocument(c.document ?? '');
    setEmail(c.email ?? ''); setPhone(c.phone ?? ''); setNotes(c.notes ?? '');
  }

  function cancelEdit() {
    setEditing(null); setName(''); setDocument(''); setEmail(''); setPhone(''); setNotes('');
  }

  async function handleSave(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!name.trim()) { setError('Informe o nome do cliente.'); return; }
    setSaving(true); setError(null); setNotice(null);
    try {
      const payload = { name: name.trim(), document: document.trim() || undefined, email: email.trim() || undefined, phone: phone.trim() || undefined, notes: notes.trim() || undefined };
      let result: ClientView;
      if (editing) {
        result = await api.updateClient(editing.id, { ...payload, active: editing.active });
        setClients(prev => prev.map(c => c.id === result.id ? result : c));
        setNotice(`Cliente "${result.name}" atualizado.`);
      } else {
        result = await api.createClient(payload);
        setClients(prev => [...prev, result]);
        setNotice(`Cliente "${result.name}" cadastrado.`);
      }
      cancelEdit();
    } catch (r: unknown) { setError(r instanceof Error ? r.message : 'Erro ao salvar.'); }
    finally { setSaving(false); }
  }

  async function handleToggle(c: ClientView) {
    try {
      const result = await api.updateClient(c.id, { name: c.name, document: c.document ?? undefined, email: c.email ?? undefined, phone: c.phone ?? undefined, notes: c.notes ?? undefined, active: !c.active });
      setClients(prev => prev.map(x => x.id === result.id ? result : x));
      setNotice(`Cliente ${result.active ? 'ativado' : 'desativado'}.`);
    } catch (r: unknown) { setError(r instanceof Error ? r.message : 'Erro.'); }
  }

  async function handleDelete(c: ClientView) {
    if (!window.confirm(`Excluir "${c.name}"? Só é possível se não houver eventos vinculados.`)) return;
    try {
      await api.deleteClient(c.id);
      setClients(prev => prev.filter(x => x.id !== c.id));
      setNotice(`Cliente "${c.name}" excluído.`);
    } catch (r: unknown) { setError(r instanceof Error ? r.message : 'Erro ao excluir.'); }
  }

  return <>
    <section className="panel full-panel">
      <div className="panel-heading">
        <div><p className="panel-kicker">{editing ? 'EDITAR' : 'NOVO'} CLIENTE</p><h2>{editing ? `Editando: ${editing.name}` : 'Cadastrar cliente'}</h2>
          <p className="panel-subtitle">Cadastro de organizadores e clientes. Cada cliente pode ter múltiplos eventos vinculados.</p></div>
        {editing && <button className="secondary-button" onClick={cancelEdit}>Cancelar</button>}
      </div>
      <form className="config-form" style={{ padding: '0 24px 24px' }} onSubmit={handleSave}>
        <div className="form-row form-row-three">
          <label>Nome do cliente *<input value={name} onChange={e => setName(e.target.value)} placeholder="Razão social ou nome fantasia" disabled={saving} /></label>
          <label>CPF/CNPJ<input value={document} onChange={e => setDocument(e.target.value)} placeholder="Somente dígitos" disabled={saving} /></label>
          <label>E-mail<input type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder="contato@empresa.com" disabled={saving} /></label>
        </div>
        <div className="form-row">
          <label>Telefone<input value={phone} onChange={e => setPhone(e.target.value)} placeholder="(11) 9 0000-0000" disabled={saving} /></label>
          <label>Observações<input value={notes} onChange={e => setNotes(e.target.value)} placeholder="Notas internas" disabled={saving} /></label>
        </div>
        {error && <div className="alert-error"><strong>{error}</strong></div>}
        {notice && <div className="alert-success"><strong>{notice}</strong></div>}
        <button className="secondary-button form-submit" type="submit" disabled={saving}>
          {saving ? 'Salvando...' : editing ? 'Salvar alterações' : 'Cadastrar cliente'}
        </button>
      </form>
    </section>

    <section className="panel full-panel">
      <div className="panel-heading"><div><p className="panel-kicker">CLIENTES CADASTRADOS</p><h2>{clients.length} cliente(s)</h2></div></div>
      {loading ? <div className="table-loading"><span className="spinner" />Carregando...</div>
        : clients.length === 0 ? <EmptyState message="Nenhum cliente cadastrado." />
        : <div className="table-scroll"><table>
            <thead><tr><th>Cliente</th><th>Documento</th><th>E-mail</th><th>Telefone</th><th>Eventos</th><th>Desde</th><th>Estado</th><th /></tr></thead>
            <tbody>{clients.map(c => (
              <tr key={c.id} style={{ opacity: c.active ? 1 : 0.55 }}>
                <td><strong>{c.name}</strong>{c.notes && <small className="table-id">{c.notes}</small>}</td>
                <td><code>{c.document || '—'}</code></td>
                <td>{c.email || '—'}</td>
                <td>{c.phone || '—'}</td>
                <td><strong>{c.eventCount}</strong></td>
                <td>{formatDate(c.createdAt)}</td>
                <td><StatusBadge value={c.active ? 'active' : 'inactive'} /></td>
                <td style={{ display: 'flex', gap: 4 }}>
                  <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px' }} onClick={() => startEdit(c)}>Editar</button>
                  <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px' }} onClick={() => handleToggle(c)}>{c.active ? 'Desativar' : 'Ativar'}</button>
                  <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px', color: '#c0392b' }} onClick={() => handleDelete(c)}>Excluir</button>
                </td>
              </tr>
            ))}</tbody>
          </table></div>}
    </section>
  </>;
}

function ReportsView({ eventId, gates: _g, sectors: _s }: { eventId: string; gates: GateView[]; sectors: SectorView[] }) {
  const [report, setReport] = useState<import('./types').ValidationReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [kiosk, setKiosk] = useState(false);

  useEffect(() => {
    if (!eventId) return;
    loadReport();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [eventId]);

  // Auto-refresh em modo kiosk
  useEffect(() => {
    if (!kiosk || !eventId) return;
    const interval = setInterval(loadReport, 30000);
    return () => clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kiosk, eventId]);

  async function loadReport() {
    if (!eventId) return;
    setLoading(true); setError(null);
    try {
      const params = fromDate ? { from: new Date(fromDate).toISOString() } : {};
      const toParams = toDate ? { ...params, to: new Date(toDate + 'T23:59:59').toISOString() } : params;
      setReport(await api.getValidationReport(eventId, toParams));
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro ao carregar relatório.'); }
    finally { setLoading(false); }
  }

  if (!eventId) return <section className="panel full-panel"><EmptyState message="Selecione um evento para visualizar os relatórios." /></section>;

  const s = report?.summary;

  return <>
    {/* Toolbar */}
    <section className="panel config-intro" style={kiosk ? { position: 'fixed', top: 0, left: 0, right: 0, zIndex: 100, borderRadius: 0 } : {}}>
      <div><p className="panel-kicker">RELATÓRIO DE VALIDAÇÃO</p><h2>{kiosk ? '⟳ Atualização automática ativa' : 'Análise de acessos'}</h2>
        <p className="panel-subtitle">Atualizado em: {report ? formatDate(report.generatedAt, true) : '—'}</p></div>
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label style={{ fontSize: 10, color: '#8d99ab', display: 'grid', gap: 4 }}>De<input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} style={{ padding: '7px 10px', border: '1px solid #dfe5ee', borderRadius: 7, fontSize: 11 }} /></label>
        <label style={{ fontSize: 10, color: '#8d99ab', display: 'grid', gap: 4 }}>Até<input type="date" value={toDate} onChange={e => setToDate(e.target.value)} style={{ padding: '7px 10px', border: '1px solid #dfe5ee', borderRadius: 7, fontSize: 11 }} /></label>
        <button className="secondary-button" onClick={loadReport} disabled={loading}>{loading ? 'Atualizando...' : '↻ Atualizar'}</button>
        <button className="secondary-button" onClick={() => setKiosk(k => !k)}>{kiosk ? '✕ Sair do kiosk' : '⛶ Kiosk'}</button>
        <a href={api.exportValidationCsv(eventId)} download className="secondary-button" style={{ textDecoration: "none", display: "inline-flex", alignItems: "center" }}>↓ CSV</a><a href={api.exportEventZip(eventId)} download className="secondary-button" style={{ textDecoration: "none", display: "inline-flex", alignItems: "center", background: "#14a876", color: "#fff" }}>⬇ ZIP Evento</a>
      </div>
    </section>

    {error && <div className="alert-error"><strong>{error}</strong></div>}

    {/* Sem cards aqui — relatórios são dados, não dashboard */}

    {loading && !report && <section className="panel full-panel"><div className="table-loading"><span className="spinner" />Carregando relatório...</div></section>}

    {report && <div className="reports-grid">
      {/* Por portaria */}
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">DESEMPENHO</p><h2>Por portaria</h2></div></div>
        {report.byGate.length === 0 ? <EmptyState message="Nenhum dado de portaria." /> : <div className="table-scroll"><table>
          <thead><tr><th>Portaria</th><th>Total</th><th>Aprovadas</th><th>Rejeitadas</th><th>Taxa</th><th>Tickets únicos</th></tr></thead>
          <tbody>{report.byGate.map(g => (
            <tr key={g.gateId}>
              <td><strong>{g.gateName}</strong></td>
              <td>{g.attempts.toLocaleString('pt-BR')}</td>
              <td style={{ color: '#20ba83' }}>{g.approved.toLocaleString('pt-BR')}</td>
              <td style={{ color: '#e66c7d' }}>{g.rejected.toLocaleString('pt-BR')}</td>
              <td><div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div style={{ width: 60, height: 5, background: '#edf0f5', borderRadius: 3, overflow: 'hidden' }}>
                  <div style={{ width: `${Math.min(g.approvalRate, 100)}%`, height: '100%', background: '#5272de', borderRadius: 3 }} />
                </div>
                <span>{g.approvalRate.toFixed(1)}%</span>
              </div></td>
              <td>{g.uniqueTickets.toLocaleString('pt-BR')}</td>
            </tr>
          ))}</tbody>
        </table></div>}
      </section>

      {/* Por setor */}
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">COBERTURA</p><h2>Por setor</h2></div></div>
        {report.bySector.length === 0 ? <EmptyState message="Nenhum setor cadastrado." /> : <div className="table-scroll"><table>
          <thead><tr><th>Setor</th><th>Total</th><th>Validados</th><th>Cobertura</th><th>Pessoas</th><th>Capacidade</th></tr></thead>
          <tbody>{report.bySector.map(sec => (
            <tr key={sec.sectorId}>
              <td><strong>{sec.sectorName}</strong></td>
              <td>{sec.totalTickets.toLocaleString('pt-BR')}</td>
              <td style={{ color: '#20ba83' }}>{sec.validatedTickets.toLocaleString('pt-BR')}</td>
              <td><div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div style={{ width: 60, height: 5, background: '#edf0f5', borderRadius: 3, overflow: 'hidden' }}>
                  <div style={{ width: `${Math.min(sec.coverageRate, 100)}%`, height: '100%', background: '#4fae8b', borderRadius: 3 }} />
                </div>
                <span>{sec.coverageRate.toFixed(1)}%</span>
              </div></td>
              <td>{sec.peopleInside.toLocaleString('pt-BR')}</td>
              <td>{sec.capacity?.toLocaleString('pt-BR') ?? '—'}</td>
            </tr>
          ))}</tbody>
        </table></div>}
      </section>

      {/* Motivos de rejeição */}
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">DIAGNÓSTICO</p><h2>Motivos de rejeição</h2></div></div>
        {report.rejectionReasons.length === 0 ? <EmptyState message="Nenhuma rejeição registrada." /> : <div className="table-scroll"><table>
          <thead><tr><th>Código</th><th>Motivo</th><th>Total</th><th>% das rejeições</th></tr></thead>
          <tbody>{report.rejectionReasons.map(r => (
            <tr key={r.code}>
              <td><code>{r.code}</code></td>
              <td>{r.reason}</td>
              <td style={{ color: '#e66c7d' }}>{r.count.toLocaleString('pt-BR')}</td>
              <td><div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div style={{ width: 80, height: 5, background: '#edf0f5', borderRadius: 3, overflow: 'hidden' }}>
                  <div style={{ width: `${Math.min(r.percentage, 100)}%`, height: '100%', background: '#e66c7d', borderRadius: 3 }} />
                </div>
                <span>{r.percentage.toFixed(1)}%</span>
              </div></td>
            </tr>
          ))}</tbody>
        </table></div>}
      </section>

      {/* Por hora */}
      {report.byHour.length > 0 && <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">DISTRIBUIÇÃO TEMPORAL</p><h2>Tentativas por hora</h2></div></div>
        <div className="table-scroll"><div style={{ padding: '0 24px 20px', display: 'flex', alignItems: 'flex-end', gap: 4, height: 140, overflowX: 'auto' }}>
          {report.byHour.map(h => {
            const maxTotal = Math.max(...report.byHour.map(x => x.total));
            const pct = maxTotal > 0 ? (h.total / maxTotal) * 100 : 0;
            return <div key={h.hour} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2, flex: '0 0 auto', minWidth: 32 }} title={`${h.hour} — ${h.total} tentativas`}>
              <span style={{ fontSize: 8, color: '#9aa6b7' }}>{h.total}</span>
              <div style={{ width: 24, background: '#e9edff', borderRadius: '3px 3px 0 0', display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', height: `${Math.max(pct * 0.9, 4)}px` }}>
                <div style={{ background: h.approved > 0 ? '#5272de' : '#e66c7d', borderRadius: '3px 3px 0 0', height: `${(h.approved / Math.max(h.total, 1)) * 100}%` }} />
              </div>
              <span style={{ fontSize: 7, color: '#b0bac7', maxWidth: 32, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{h.hour.slice(11, 16)}</span>
            </div>;
          })}
        </div></div>
      </section>}

      {/* Últimas tentativas */}
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">MONITORAMENTO</p><h2>Últimas tentativas</h2></div></div>
        {report.recentAttempts.length === 0 ? <EmptyState message="Nenhuma tentativa registrada." /> : <div className="table-scroll"><table>
          <thead><tr><th>Horário</th><th>Credencial</th><th>Portaria / Setor</th><th>Direção</th><th>Decisão</th></tr></thead>
          <tbody>{report.recentAttempts.map(a => (
            <tr key={a.attemptId}>
              <td>{formatDate(a.requestedAt, true)}</td>
              <td><strong>{a.ticketExternalId || a.credentialCode}</strong><small className="table-id">{a.credentialCode}</small></td>
              <td><strong>{a.gateName || '—'}</strong><small>{a.sectorName || ''}</small></td>
              <td><span className="direction">{a.direction === 'Entry' ? '↓ Entrada' : '↑ Saída'}</span></td>
              <td><StatusBadge value={a.decision} /></td>
            </tr>
          ))}</tbody>
        </table></div>}
      </section>
    </div>}
  </>;
}

function LoginView({ onLogin }: { onLogin: (session: SessionView) => void }) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!userName.trim() || !password) { setError('Informe o usuário e a senha.'); return; }
    setLoading(true); setError(null);
    try {
      const session = await api.login(userName.trim(), password);
      onLogin(session);
    } catch {
      setError('Usuário ou senha inválidos.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-shell">
      <form className="login-card" onSubmit={handleSubmit}>
        <div className="login-brand">
          <img src="/fastpass-logo-white.png" alt="FastPass" className="login-logo" />
          <span>V2 Console</span>
        </div>
        <h1>Entrar</h1>
        <p>Acesse com suas credenciais de operador.</p>
        {error && <div className="alert-error"><strong>{error}</strong></div>}
        <label>Usuário<input autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} placeholder="admin" disabled={loading} /></label>
        <label>Senha<input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••••" disabled={loading} /></label>
        <button className="primary-button form-submit" type="submit" disabled={loading}>{loading ? 'Entrando...' : 'Entrar'}</button>
      </form>
    </div>
  );
}

function UsersView() {
  const [users, setUsers] = useState<UserView[]>([]);
  const [roles, setRoles] = useState<RoleView[]>([]);
  const [permissions, setPermissions] = useState<PermissionView[]>([]);
  const [events, setEvents] = useState<EventView[]>([]);
  const [gates, setGates] = useState<GateView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [tab, setTab] = useState<'users' | 'roles'>('users');

  // Formulário usuário
  const [editingUser, setEditingUser] = useState<UserView | null>(null);
  const [newUserName, setNewUserName] = useState('');
  const [newDisplayName, setNewDisplayName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRoleIds, setNewRoleIds] = useState<string[]>([]);
  const [newEventScope, setNewEventScope] = useState<'Todos' | 'Ativos' | 'Especificos'>('Todos');
  const [newEventIds, setNewEventIds] = useState<string[]>([]);
  const [newGateIds, setNewGateIds] = useState<string[]>([]);
  const [savingUser, setSavingUser] = useState(false);
  // Acesso físico no usuário
  const [physicalAccess, setPhysicalAccess] = useState(false);
  const [badgeCode, setBadgeCode] = useState('');

  // Formulário perfil
  const [newRoleName, setNewRoleName] = useState('');
  const [newRoleDesc, setNewRoleDesc] = useState('');
  const [savingRole, setSavingRole] = useState(false);
  const [savingPermRoleId, setSavingPermRoleId] = useState<string | null>(null);

  // Ao selecionado um evento, busca as portarias
  const selectedEventId = events[0]?.id ?? '';

  useEffect(() => {
    setLoading(true);
    Promise.all([
      api.listUsers(false), api.listRoles(), api.listPermissions(),
      api.listEvents(), api.listGates(selectedEventId).catch(() => [] as GateView[])
    ])
      .then(([u, r, p, ev]) => { setUsers(u); setRoles(r); setPermissions(p); setEvents(ev); })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Erro ao carregar.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    if (!selectedEventId) return;
    api.listGates(selectedEventId).then(setGates).catch(() => {});
  }, [selectedEventId]);

  function startEditUser(user: UserView) {
    setEditingUser(user);
    setNewDisplayName(user.displayName);
    setNewPassword('');
    setNewRoleIds(user.roles.map(r => r.id));
    setNewEventScope(user.eventScope);
    setNewEventIds(user.authorizedEventIds);
    setNewGateIds(user.authorizedGateIds);
    setPhysicalAccess(user.physicalAccessEnabled ?? false);
    setBadgeCode(user.accessBadgeCode ?? '');
    setError(null);
  }

  function cancelEditUser() {
    setEditingUser(null);
    setNewUserName(''); setNewDisplayName(''); setNewPassword('');
    setNewRoleIds([]); setNewEventScope('Todos'); setNewEventIds([]); setNewGateIds([]);
    setPhysicalAccess(false); setBadgeCode('');
  }

  async function handleSaveUser(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!editingUser && (!newUserName.trim() || !newDisplayName.trim() || !newPassword)) {
      setError('Preencha usuário, nome e senha.'); return;
    }
    if (newRoleIds.length === 0) { setError('Atribua pelo menos um perfil.'); return; }
    if (newEventScope === 'Especificos' && newEventIds.length === 0) {
      setError('Selecione ao menos um evento no modo Específicos.'); return;
    }
    if (physicalAccess && !badgeCode.trim()) {
      setError('Informe o código de acesso para habilitar o acesso físico.'); return;
    }
    setSavingUser(true); setError(null); setNotice(null);
    try {
      const payload = {
        displayName: newDisplayName.trim(), active: true, roleIds: newRoleIds,
        eventScope: newEventScope, eventIds: newEventIds, gateIds: newGateIds,
        physicalAccessEnabled: physicalAccess,
        accessBadgeCode: physicalAccess ? badgeCode.trim() : undefined,
      };
      let result: UserView;
      if (editingUser) {
        result = await api.updateUser(editingUser.id, payload);
      } else {
        result = await api.createUser({ userName: newUserName.trim(), password: newPassword, ...payload });
      }
      setUsers(prev => editingUser ? prev.map(u => u.id === result.id ? result : u) : [...prev, result]);
      setNotice(`Usuário "${result.displayName}" ${editingUser ? 'atualizado' : 'criado'}${physicalAccess ? ` · crachá: ${badgeCode}` : ''}.`);
      cancelEditUser();
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro ao salvar.'); }
    finally { setSavingUser(false); }
  }

  async function handleToggleUser(user: UserView) {
    try {
      const result = await api.updateUser(user.id, {
        displayName: user.displayName, active: !user.active,
        roleIds: user.roles.map(r => r.id), eventScope: user.eventScope,
        eventIds: user.authorizedEventIds, gateIds: user.authorizedGateIds
      });
      setUsers(prev => prev.map(u => u.id === result.id ? result : u));
      setNotice(`Usuário ${result.active ? 'ativado' : 'desativado'}.`);
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro.'); }
  }

  async function handleUnblock(userId: string) {
    try {
      await api.unblockUser(userId);
      setUsers(prev => prev.map(u => u.id === userId ? { ...u, blockedUntil: null } : u));
      setNotice('Usuário desbloqueado.');
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro.'); }
  }

  async function handleCreateRole(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!newRoleName.trim()) { setError('Informe o nome do perfil.'); return; }
    setSavingRole(true); setError(null); setNotice(null);
    try {
      const created = await api.createRole({ name: newRoleName.trim(), description: newRoleDesc.trim() || undefined });
      setRoles(prev => [...prev, created]);
      setNewRoleName(''); setNewRoleDesc('');
      setNotice(`Perfil "${created.name}" criado.`);
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro.'); }
    finally { setSavingRole(false); }
  }

  async function handleDeleteRole(role: RoleView) {
    if (!window.confirm(`Excluir o perfil "${role.name}"?`)) return;
    try {
      await api.deleteRole(role.id);
      setRoles(prev => prev.filter(r => r.id !== role.id));
      setNotice(`Perfil "${role.name}" excluído.`);
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro ao excluir.'); }
  }

  async function handleTogglePermission(role: RoleView, permCode: string) {
    if (role.systemRole) return;
    const updated = role.permissions.includes(permCode)
      ? role.permissions.filter(p => p !== permCode)
      : [...role.permissions, permCode];
    setSavingPermRoleId(role.id);
    try {
      const result = await api.setRolePermissions(role.id, updated);
      setRoles(prev => prev.map(r => r.id === result.id ? result : r));
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Erro ao atualizar permissão.'); }
    finally { setSavingPermRoleId(null); }
  }

  // Agrupa permissões em categorias para exibição
  const permCategories: Record<string, string[]> = {
    'Validação': ['acesso.validar'],
    'Relatórios': ['relatorio.ler', 'acessos.ler', 'auditoria.ler'],
    'Catálogo': ['cliente.gerenciar', 'evento.criar', 'evento.editar', 'evento.excluir', 'portaria.gerenciar', 'setor.gerenciar', 'relacao.gerenciar', 'dispositivo.gerenciar'],
    'Ingressos': ['ticket.consultar', 'ticket.status', 'ticket.importar', 'ticket.dados.excluir'],
    'Mensagens': ['mensagem.gerenciar'],
    'Administração': ['usuario.gerenciar', 'perfil.gerenciar', 'perfil.permissoes.gerenciar', 'sessao.revogar', 'escopo.global'],
  };

  const isBlocked = (user: UserView) => user.blockedUntil && new Date(user.blockedUntil) > new Date();

  return <>
    <div className="users-tabs">
      <button className={tab === 'users' ? 'users-tab active' : 'users-tab'} onClick={() => { setTab('users'); cancelEditUser(); }}>
        Usuários <span className="users-tab-count">{users.length}</span>
      </button>
      <button className={tab === 'roles' ? 'users-tab active' : 'users-tab'} onClick={() => setTab('roles')}>
        Perfis e Permissões <span className="users-tab-count">{roles.length}</span>
      </button>
    </div>
    {error && <div className="alert-error"><strong>{error}</strong></div>}
    {notice && <div className="alert-success"><strong>{notice}</strong></div>}

    {loading
      ? <section className="panel full-panel"><div className="table-loading"><span className="spinner" />Carregando...</div></section>
      : tab === 'users' ? <>
          {/* Formulário usuário */}
          <section className="panel full-panel">
            <div className="panel-heading">
              <div><p className="panel-kicker">{editingUser ? 'EDITAR' : 'NOVO'} USUÁRIO</p><h2>{editingUser ? `Editando: ${editingUser.displayName}` : 'Cadastrar usuário'}</h2></div>
              {editingUser && <button className="secondary-button" onClick={cancelEditUser}>Cancelar edição</button>}
            </div>
            <form className="config-form" style={{ padding: '0 24px 24px' }} onSubmit={handleSaveUser}>
              <div className="form-row form-row-three">
                {!editingUser && <label>Usuário (login)<input value={newUserName} onChange={e => setNewUserName(e.target.value)} placeholder="nome.sobrenome" disabled={savingUser} /></label>}
                <label>Nome de exibição<input value={newDisplayName} onChange={e => setNewDisplayName(e.target.value)} placeholder="Nome Completo" disabled={savingUser} /></label>
                {!editingUser && <label>Senha inicial<input type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} placeholder="Mín. 8 caracteres" disabled={savingUser} /></label>}
              </div>
              {/* Perfis */}
              <div>
                <span className="field-caption">Perfis</span>
                <div className="direction-options">
                  {roles.map(role => (
                    <label key={role.id} className="form-checkbox">
                      <input type="checkbox" checked={newRoleIds.includes(role.id)}
                        onChange={e => setNewRoleIds(e.target.checked ? [...newRoleIds, role.id] : newRoleIds.filter(id => id !== role.id))}
                        disabled={savingUser} />
                      <span className="checkbox-box">✓</span>
                      {role.name}{role.systemRole && <em style={{ fontSize: 9, color: '#9571df', marginLeft: 4 }}>sistema</em>}
                    </label>
                  ))}
                </div>
              </div>
              {/* Escopo de eventos */}
              <div>
                <span className="field-caption">Escopo de eventos</span>
                <div className="direction-options">
                  {(['Todos', 'Ativos', 'Especificos'] as const).map(mode => (
                    <label key={mode} className="form-checkbox">
                      <input type="radio" name="eventScope" value={mode} checked={newEventScope === mode}
                        onChange={() => setNewEventScope(mode)} disabled={savingUser} />
                      <span className="checkbox-box">✓</span>
                      {mode === 'Todos' ? 'Todos os eventos' : mode === 'Ativos' ? 'Somente eventos ativos' : 'Eventos específicos'}
                    </label>
                  ))}
                </div>
                {newEventScope === 'Especificos' && (
                  <div style={{ marginTop: 8 }}>
                    <span className="field-caption">Selecione os eventos</span>
                    <div className="direction-options" style={{ flexWrap: 'wrap', gap: 6 }}>
                      {events.map(ev => (
                        <label key={ev.id} className="form-checkbox">
                          <input type="checkbox" checked={newEventIds.includes(ev.id)}
                            onChange={e => setNewEventIds(e.target.checked ? [...newEventIds, ev.id] : newEventIds.filter(id => id !== ev.id))}
                            disabled={savingUser} />
                          <span className="checkbox-box">✓</span>{ev.name}
                        </label>
                      ))}
                    </div>
                  </div>
                )}
              </div>
              {/* Portarias autorizadas */}
              {gates.length > 0 && (
                <div>
                  <span className="field-caption">Portarias autorizadas <em style={{ color: '#9aa6b7', fontWeight: 400 }}>(sem seleção = acesso pelo escopo.global)</em></span>
                  <div className="direction-options" style={{ flexWrap: 'wrap', gap: 6 }}>
                    {gates.map(gate => (
                      <label key={gate.id} className="form-checkbox">
                        <input type="checkbox" checked={newGateIds.includes(gate.id)}
                          onChange={e => setNewGateIds(e.target.checked ? [...newGateIds, gate.id] : newGateIds.filter(id => id !== gate.id))}
                          disabled={savingUser} />
                        <span className="checkbox-box">✓</span>{gate.name}{gate.code && <small style={{ color: '#9aa6b7' }}> {gate.code}</small>}
                      </label>
                    ))}
                  </div>
                </div>
              )}
              {/* Acesso físico — crachá na catraca */}
              <div style={{ borderTop: '1px solid #edf0f5', marginTop: 16, paddingTop: 16 }}>
                <label className="form-checkbox" style={{ marginBottom: 10 }}>
                  <input type="checkbox" checked={physicalAccess} onChange={e => setPhysicalAccess(e.target.checked)} disabled={savingUser} />
                  <span className="checkbox-box">✓</span>
                  <span>
                    <strong>Acesso físico à catraca</strong>
                    <small style={{ color: '#8d99ab', display: 'block', fontWeight: 400 }}>
                      Este usuário possui crachá/QR que libera a catraca. Não conta como ingresso validado — controlado separadamente.
                    </small>
                  </span>
                </label>
                {physicalAccess && (
                  <div className="form-row" style={{ marginTop: 8 }}>
                    <label>Código do crachá / QR *
                      <input value={badgeCode} onChange={e => setBadgeCode(e.target.value)}
                        placeholder="Código impresso ou gerado — lido pela catraca"
                        disabled={savingUser} style={{ fontFamily: 'monospace' }} />
                      <small style={{ color: '#8d99ab', fontSize: 10 }}>Deve ser único em todo o sistema</small>
                    </label>
                    <label style={{ color: '#20ba83', fontWeight: 700, fontSize: 10, alignSelf: 'end', paddingBottom: 6 }}>
                      {editingUser?.accessBadgeCode
                        ? '✓ Crachá atualizado'
                        : '+ Novo crachá'}
                    </label>
                  </div>
                )}
              </div>
              <button className="secondary-button form-submit" type="submit" disabled={savingUser}>
                {savingUser ? 'Salvando...' : editingUser ? 'Salvar alterações' : 'Criar usuário'}
              </button>
            </form>
          </section>

          {/* Lista de usuários */}
          <section className="panel full-panel">
            <div className="panel-heading"><div><p className="panel-kicker">LISTA</p><h2>{users.length} usuário(s)</h2></div></div>
            {users.length === 0 ? <EmptyState message="Nenhum usuário cadastrado." /> : (
              <div className="table-scroll"><table>
                <thead><tr><th>Usuário</th><th>Login</th><th>Perfis</th><th>Escopo</th><th>Crachá</th><th>Último acesso</th><th>Estado</th><th /></tr></thead>
                <tbody>{users.map(user => (
                  <tr key={user.id} style={{ opacity: user.active ? 1 : 0.55 }}>
                    <td><strong>{user.displayName}</strong><small className="table-id">{user.id}</small></td>
                    <td><code>{user.userName}</code></td>
                    <td><small>{user.roles.map(r => r.name).join(', ') || '—'}</small></td>
                    <td>
                      <small>{user.eventScope === 'Especificos'
                        ? `${user.authorizedEventIds.length} evento(s)`
                        : user.eventScope === 'Ativos' ? 'Só ativos' : 'Todos'}</small>
                    </td>
                    <td>{user.physicalAccessEnabled
                      ? <code style={{ fontSize: 10, background: '#f0f3ff', padding: '2px 6px', borderRadius: 4, color: '#3b6fde' }}>{user.accessBadgeCode}</code>
                      : <span style={{ color: '#bcc3cf' }}>—</span>}
                    </td>
                    <td>{user.lastLoginAt ? formatDate(user.lastLoginAt, true) : '—'}</td>
                    <td><StatusBadge value={user.active ? 'active' : 'inactive'} /></td>
                    <td className="table-actions" style={{ display: 'flex', gap: 4 }}>
                      <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px' }}
                        onClick={() => startEditUser(user)}>Editar</button>
                      <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px' }}
                        onClick={() => handleToggleUser(user)}>{user.active ? 'Desativar' : 'Ativar'}</button>
                      {isBlocked(user) && (
                        <button className="secondary-button" style={{ fontSize: 9, padding: '4px 8px', color: '#e66c7d' }}
                          onClick={() => handleUnblock(user.id)}>Desbloquear</button>
                      )}
                    </td>
                  </tr>
                ))}</tbody>
              </table></div>
            )}
          </section>
        </>
      : /* ═══════════ PERFIS E PERMISSÕES ═══════════ */ <>
          {/* Novo perfil */}
          <section className="panel full-panel">
            <div className="panel-heading"><div><p className="panel-kicker">NOVO PERFIL</p><h2>Criar perfil</h2></div></div>
            <form className="config-form" style={{ padding: '0 24px 24px' }} onSubmit={handleCreateRole}>
              <div className="form-row">
                <label>Nome<input value={newRoleName} onChange={e => setNewRoleName(e.target.value)} placeholder="Ex.: Operador de portaria" disabled={savingRole} /></label>
                <label>Descrição (opcional)<input value={newRoleDesc} onChange={e => setNewRoleDesc(e.target.value)} placeholder="Breve descrição do perfil" disabled={savingRole} /></label>
              </div>
              <button className="secondary-button form-submit" type="submit" disabled={savingRole}>
                {savingRole ? 'Criando...' : 'Criar perfil'}
              </button>
            </form>
          </section>

          {/* Matriz visual permissões × perfis */}
          <section className="panel full-panel">
            <div className="panel-heading">
              <div><p className="panel-kicker">MATRIZ DE PERMISSÕES</p><h2>Perfis × Permissões</h2>
                <p className="panel-subtitle">Clique nas células para conceder/revogar. Perfis de sistema são somente leitura.</p></div>
            </div>
            <div className="perm-matrix-scroll">
              <table className="perm-matrix">
                <thead>
                  <tr>
                    <th className="perm-matrix-label">Permissão</th>
                    {roles.map(role => (
                      <th key={role.id} className={role.systemRole ? 'perm-matrix-role perm-matrix-system' : 'perm-matrix-role'}>
                        <span>{role.name}</span>
                        {role.systemRole ? <em>sistema</em> : (
                          <button className="matrix-remove-button" onClick={() => handleDeleteRole(role)}>Excluir</button>
                        )}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {Object.entries(permCategories).map(([category, codes]) => {
                    const catPerms = codes.filter(code => permissions.some(p => p.code === code));
                    if (catPerms.length === 0) return null;
                    return [
                      <tr key={`cat-${category}`} className="perm-matrix-category">
                        <td colSpan={roles.length + 1}>{category}</td>
                      </tr>,
                      ...catPerms.map(code => {
                        const perm = permissions.find(p => p.code === code);
                        if (!perm) return null;
                        return (
                          <tr key={code} className={perm.privileged ? 'perm-row perm-privileged' : 'perm-row'}>
                            <td className="perm-name" title={perm.description}>
                              <code>{perm.code}</code>
                              {perm.privileged && <span className="perm-priv-badge">★</span>}
                            </td>
                            {roles.map(role => {
                              const has = role.permissions.includes(code);
                              const saving = savingPermRoleId === role.id;
                              return (
                                <td key={role.id} className={`perm-cell ${has ? 'perm-cell-on' : 'perm-cell-off'}`}>
                                  <label className="form-checkbox" style={{ justifyContent: 'center' }}>
                                    <input type="checkbox" checked={has}
                                      disabled={role.systemRole || saving}
                                      onChange={() => handleTogglePermission(role, code)} />
                                    <span className="checkbox-box">✓</span>
                                  </label>
                                </td>
                              );
                            })}
                          </tr>
                        );
                      })
                    ];
                  })}
                </tbody>
              </table>
            </div>
          </section>
        </>
    }
  </>;
}

function ImportLogsView({ eventId }: { eventId: string }) {
  const [history, setHistory] = useState<import('./types').ImportView[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [detail, setDetail] = useState<{ import: import('./types').ImportView; rows: import('./types').ImportRowView[] } | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [rowFilter, setRowFilter] = useState<'all' | 'inserted' | 'updated' | 'skipped' | 'error'>('all');

  useEffect(() => {
    if (!eventId) { setHistory([]); return; }
    setLoading(true); setError(null);
    api.listImports(eventId).then(setHistory).catch((r: unknown) => setError(r instanceof Error ? r.message : 'Erro')).finally(() => setLoading(false));
  }, [eventId]);

  async function openDetail(importId: string) {
    if (openId === importId) { setOpenId(null); setDetail(null); return; }
    setOpenId(importId); setDetail(null); setLoadingDetail(true); setRowFilter('all');
    try {
      const d = await api.getImportDetail(eventId, importId);
      setDetail(d);
    } catch (r: unknown) { setError(r instanceof Error ? r.message : 'Erro ao carregar detalhe.'); }
    finally { setLoadingDetail(false); }
  }

  if (!eventId) return <section className="panel full-panel"><EmptyState message="Selecione um evento para consultar o log de importações." /></section>;

  const filteredRows = detail?.rows.filter(r => rowFilter === 'all' || r.status === rowFilter) ?? [];

  return <>
    <section className="panel config-intro"><div><p className="panel-kicker">LOG</p><h2>Importações de ingressos</h2>
      <p className="panel-subtitle">Histórico completo. Clique em uma importação para ver o resultado linha a linha.</p></div>
      <div className="config-counts"><strong>{history.length}</strong><span>importações</span></div>
    </section>
    {error && <div className="alert-error"><strong>{error}</strong></div>}

    <section className="panel full-panel">
      {loading ? <div className="table-loading"><span className="spinner" />Carregando...</div>
        : history.length === 0 ? <EmptyState message="Nenhuma importação registrada para este evento." />
        : <div className="table-scroll"><table>
            <thead><tr><th /><th>Arquivo</th><th>Modo</th><th>Total</th><th>Inseridos</th><th>Atualizados</th><th>Ignorados</th><th>Erros</th><th>Status</th><th>Data</th></tr></thead>
            <tbody>{history.map(h => <Fragment key={h.id}>
              <tr style={{ cursor: 'pointer' }} onClick={() => openDetail(h.id)}>
                <td style={{ width: 20, color: '#9aa6b7' }}>{openId === h.id ? '▾' : '▸'}</td>
                <td><strong>{h.fileName}</strong><small className="table-id">{h.id}</small></td>
                <td><small>{h.mode}</small></td>
                <td>{h.totalRows}</td>
                <td style={{ color: '#20ba83' }}>{h.inserted}</td>
                <td style={{ color: '#5272de' }}>{h.updated}</td>
                <td style={{ color: '#9aa6b7' }}>{h.skipped}</td>
                <td style={{ color: h.errors > 0 ? '#e66c7d' : '#9aa6b7' }}>{h.errors}</td>
                <td><StatusBadge value={h.status === 'done' ? 'Approved' : h.status === 'failed' ? 'Rejected' : 'neutral'} /></td>
                <td>{formatDate(h.createdAt, true)}</td>
              </tr>
              {openId === h.id && (
                <tr>
                  <td colSpan={10} style={{ background: '#fafbfe', padding: 0 }}>
                    <div style={{ padding: '16px 20px' }}>
                      {loadingDetail ? <div className="table-loading"><span className="spinner" />Carregando linhas...</div>
                        : detail && (
                          <>
                            <div style={{ display: 'flex', gap: 6, marginBottom: 12, flexWrap: 'wrap' }}>
                              {(['all', 'inserted', 'updated', 'skipped', 'error'] as const).map(f => (
                                <button key={f} className={rowFilter === f ? 'secondary-button' : 'secondary-button'}
                                  style={{ fontSize: 10, padding: '5px 10px', opacity: rowFilter === f ? 1 : 0.55 }}
                                  onClick={() => setRowFilter(f)}>
                                  {f === 'all' ? `Todas (${detail.rows.length})`
                                    : f === 'inserted' ? `Inseridas (${detail.import.inserted})`
                                    : f === 'updated' ? `Atualizadas (${detail.import.updated})`
                                    : f === 'skipped' ? `Ignoradas (${detail.import.skipped})`
                                    : `Erros (${detail.import.errors})`}
                                </button>
                              ))}
                            </div>
                            {filteredRows.length === 0 ? <EmptyState message="Nenhuma linha nesta categoria." /> : (
                              <div className="table-scroll" style={{ maxHeight: 380, overflowY: 'auto' }}>
                                <table style={{ fontSize: 11 }}>
                                  <thead><tr><th>Linha</th><th>Código</th><th>ID externo</th><th>Setor</th><th>Qtd. entradas</th><th>Status</th><th>Detalhe</th></tr></thead>
                                  <tbody>{filteredRows.map(r => (
                                    <tr key={r.rowNumber}>
                                      <td>{r.rowNumber}</td>
                                      <td><code>{r.code}</code></td>
                                      <td>{r.externalId || '—'}</td>
                                      <td>{r.sectorName || <span style={{ color: '#bcc3cf' }}>—</span>}</td>
                                      <td>{r.maxEntries}</td>
                                      <td><StatusBadge value={r.status === 'inserted' || r.status === 'updated' ? 'Approved' : r.status === 'error' ? 'Rejected' : 'neutral'} /></td>
                                      <td style={{ color: '#8d99ab' }}>{r.errorDetail || '—'}</td>
                                    </tr>
                                  ))}</tbody>
                                </table>
                              </div>
                            )}
                          </>
                        )}
                    </div>
                  </td>
                </tr>
              )}
            </Fragment>)}</tbody>
          </table></div>}
    </section>
  </>;
}

function ImportView({ eventId }: { eventId: string }) {
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [preview, setPreview] = useState<import('./types').CsvPreviewResult | null>(null);
  const [previewing, setPreviewing] = useState(false);
  // Configuração
  const [separator, setSeparator] = useState(',');
  const [colCode, setColCode] = useState(0);
  const [colExtId, setColExtId] = useState<string>('');
  const [colMaxEnt, setColMaxEnt] = useState<string>('');
  const [defaultMaxEnt, setDefaultMaxEnt] = useState(1);
  const [mode, setMode] = useState('AdicionarAtualizar');
  const [colStatus, setColStatus] = useState<string>('');
  const [defaultStatus, setDefaultStatus] = useState('active');
  const [colSector, setColSector] = useState<string>('');
  const [defaultSectorId, setDefaultSectorId] = useState<string>('');
  const [sectors, setSectors] = useState<SectorView[]>([]);
  // Estado
  const [importing, setImporting] = useState(false);
  const [result, setResult] = useState<import('./types').ImportView | null>(null);
  const [history, setHistory] = useState<import('./types').ImportView[]>([]);
  const [loadingHistory, setLoadingHistory] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [unknownSectors, setUnknownSectors] = useState<string[] | null>(null);
  const [creatingSectors, setCreatingSectors] = useState(false);

  useEffect(() => {
    if (!eventId) return;
    setLoadingHistory(true);
    api.listImports(eventId).then(setHistory).catch(() => {}).finally(() => setLoadingHistory(false));
  }, [eventId, result]);

  useEffect(() => {
    if (!eventId) return;
    api.listSectors(eventId).then(setSectors).catch(() => {});
  }, [eventId]);

  async function handleFile(f: File) {
    setFile(f); setPreview(null); setResult(null); setError(null);
    setPreviewing(true);
    try {
      const prev = await api.previewImport(eventId, f);
      setPreview(prev);
      setSeparator(prev.detectedSeparator === '\\t' ? '\\t' : prev.detectedSeparator);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Erro ao visualizar o arquivo.');
    } finally { setPreviewing(false); }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault(); setDragging(false);
    const f = e.dataTransfer.files[0];
    if (f) handleFile(f);
  }

  async function runImport(currentFile: File) {
    setImporting(true); setError(null); setResult(null); setUnknownSectors(null);
    try {
      const imp = await api.startImport(eventId, currentFile, {
        separator, colCode, mode,
        colExternalId: colExtId !== '' ? Number(colExtId) : undefined,
        colMaxEntries: colMaxEnt !== '' ? Number(colMaxEnt) : undefined,
        defaultMaxEntries: defaultMaxEnt,
        colStatus: colStatus !== '' ? Number(colStatus) : undefined,
        defaultStatus,
        colSector: colSector !== '' ? Number(colSector) : undefined,
        defaultSectorId: defaultSectorId || undefined,
      });
      setResult(imp);
      setFile(null); setPreview(null);
    } catch (reason: unknown) {
      const err = reason as (Error & { unknownSectorNames?: string[] }) | unknown;
      if (err instanceof Error) {
        setError(err.message);
        const withSectors = err as Error & { unknownSectorNames?: string[] };
        if (withSectors.unknownSectorNames?.length) setUnknownSectors(withSectors.unknownSectorNames);
      } else {
        setError('Erro ao importar.');
      }
    } finally { setImporting(false); }
  }

  async function handleImport(e: React.FormEvent) {
    e.preventDefault();
    if (!file || !eventId) return;
    await runImport(file);
  }

  /** Cria no evento os setores citados no CSV que ainda não existem, então repete a mesma importação. */
  async function handleCreateMissingSectors() {
    if (!unknownSectors || !file) return;
    setCreatingSectors(true); setError(null);
    try {
      const createdSectors = await Promise.all(
        unknownSectors.map((name) => api.createSector(eventId, { name })),
      );
      setSectors((current) => [...current, ...createdSectors].sort((a, b) => a.name.localeCompare(b.name)));
      setUnknownSectors(null);
      await runImport(file);
    } catch (reason: unknown) {
      setError(reason instanceof Error ? `Não foi possível cadastrar os setores: ${reason.message}` : 'Não foi possível cadastrar os setores.');
    } finally { setCreatingSectors(false); }
  }

  if (!eventId) return <section className="panel full-panel"><EmptyState message="Selecione um evento para importar ingressos." /></section>;

  const colLabels = preview?.headers.map((h, i) => `[${i}] ${h || `Coluna ${i}`}`) ?? [];

  return <>
    {/* Drop zone */}
    <section className="panel full-panel">
      <div className="panel-heading"><div><p className="panel-kicker">IMPORTAÇÃO</p><h2>Carregar arquivo CSV</h2>
        <p className="panel-subtitle">Formatos suportados: CSV com vírgula, ponto-e-vírgula, tab ou pipe. UTF-8 ou Latin-1.</p></div></div>
      <div
        className={`import-dropzone${dragging ? ' import-dropzone-active' : ''}`}
        onDragOver={e => { e.preventDefault(); setDragging(true); }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        onClick={() => document.getElementById('import-file-input')?.click()}
      >
        <input id="import-file-input" type="file" accept=".csv,.txt" style={{ display: 'none' }}
          onChange={e => { const f = e.target.files?.[0]; if (f) handleFile(f); }} />
        {previewing
          ? <><span className="spinner" /><p>Analisando arquivo...</p></>
          : file
            ? <><span style={{ fontSize: 28 }}>📄</span><p><strong>{file.name}</strong></p><small>{(file.size / 1024).toFixed(1)} KB · {preview?.totalRows ?? '—'} linha(s) de dados</small></>
            : <><span style={{ fontSize: 32 }}>↑</span><p>Arraste o CSV aqui ou clique para selecionar</p></>}
      </div>
    </section>

    {error && (
      <div className="alert-error">
        <strong>{unknownSectors?.length ? 'Importação bloqueada — setores não cadastrados' : 'Não foi possível importar.'}</strong>
        <span>{error}</span>
        {unknownSectors && unknownSectors.length > 0 && (
          <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {unknownSectors.map(name => <code key={name} style={{ background: '#fff5f6', padding: '3px 8px', borderRadius: 5, fontSize: 11 }}>{name}</code>)}
            </div>
            <div>
              <button type="button" className="secondary-button" disabled={creatingSectors} onClick={handleCreateMissingSectors}>
                {creatingSectors ? 'Cadastrando setores...' : `+ Cadastrar ${unknownSectors.length} setor(es) automaticamente e importar`}
              </button>
              <small style={{ display: 'block', marginTop: 6, color: '#9aa6b7' }}>
                Cria cada setor com o nome exato do arquivo (sem capacidade definida) e repete a importação.
              </small>
            </div>
          </div>
        )}
      </div>
    )}

    {/* Preview + configuração */}
    {preview && file && (
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">CONFIGURAÇÃO</p><h2>Mapeamento de colunas</h2>
          <p className="panel-subtitle">Separador detectado: <strong>{preview.detectedSeparator}</strong> · {preview.totalRows} linha(s) de dados</p></div></div>

        {/* Tabela de amostra */}
        <div className="table-scroll" style={{ padding: '0 24px 16px' }}>
          <table style={{ fontSize: 10 }}>
            <thead><tr>{preview.headers.map((h, i) => <th key={i}>[{i}] {h || '—'}</th>)}</tr></thead>
            <tbody>{preview.sampleRows.map((row, ri) => (
              <tr key={ri}>{row.map((cell, ci) => <td key={ci}>{cell || '—'}</td>)}</tr>
            ))}</tbody>
          </table>
        </div>

        <form className="config-form" style={{ padding: '0 24px 24px' }} onSubmit={handleImport}>
          <div className="form-row form-row-three">
            <label>Separador
              <select value={separator} onChange={e => setSeparator(e.target.value)} disabled={importing}>
                <option value=",">, (vírgula)</option>
                <option value=";">; (ponto-e-vírgula)</option>
                <option value="\t">Tab</option>
                <option value="|">| (pipe)</option>
              </select>
            </label>
            <label>Coluna do código *
              <select value={colCode} onChange={e => setColCode(Number(e.target.value))} disabled={importing}>
                {colLabels.map((l, i) => <option key={i} value={i}>{l}</option>)}
              </select>
            </label>
            <label>Coluna do ID externo
              <select value={colExtId} onChange={e => setColExtId(e.target.value)} disabled={importing}>
                <option value="">— usar o código —</option>
                {colLabels.map((l, i) => <option key={i} value={i}>{l}</option>)}
              </select>
            </label>
          </div>
          <div className="form-row form-row-three">
            <label>Coluna de max. entradas
              <select value={colMaxEnt} onChange={e => setColMaxEnt(e.target.value)} disabled={importing}>
                <option value="">— usar padrão —</option>
                {colLabels.map((l, i) => <option key={i} value={i}>{l}</option>)}
              </select>
            </label>
            <label>Entradas padrão
              <input type="number" min={1} value={defaultMaxEnt}
                onChange={e => setDefaultMaxEnt(Number(e.target.value))} disabled={importing} />
            </label>
            <label>Modo
              <select value={mode} onChange={e => setMode(e.target.value)} disabled={importing}>
                <option value="AdicionarAtualizar">Adicionar e atualizar</option>
                <option value="SomenteAdicionar">Somente adicionar (novos)</option>
                <option value="SomenteAtualizar">Somente atualizar (existentes)</option>
              </select>
            </label>
          </div>
          <div className="form-row form-row-three">
            <label>
              Status padrão <small style={{ color: '#5272de', fontWeight: 700 }}>Lista branca / Lista negra</small>
              <select value={defaultStatus} onChange={e => setDefaultStatus(e.target.value)} disabled={importing}>
                <option value="active">✅ Ativo — lista branca (tickets válidos)</option>
                <option value="cancelled">🚫 Cancelado — lista negra (tickets bloqueados)</option>
                <option value="revoked">⛔ Revogado</option>
              </select>
              <small style={{ color: '#9aa6b7', fontSize: 9, marginTop: 3, display: 'block' }}>
                Status aplicado a todos os tickets do arquivo. Combine com "Adicionar e atualizar" para sincronizar uma lista completa.
              </small>
            </label>
            <label>Coluna de status (opcional)
              <select value={colStatus} onChange={e => setColStatus(e.target.value)} disabled={importing}>
                <option value="">— usar status padrão —</option>
                {colLabels.map((l, i) => <option key={i} value={i}>{l}</option>)}
              </select>
              <small style={{ color: '#9aa6b7', fontSize: 9, marginTop: 3, display: 'block' }}>
                Se informada, cada linha define seu próprio status (ativo/cancelado/revogado).
              </small>
            </label>
          </div>
          <div className="form-row form-row-three">
            <label>Coluna do setor (opcional)
              <select value={colSector} onChange={e => setColSector(e.target.value)} disabled={importing}>
                <option value="">— usar setor padrão —</option>
                {colLabels.map((l, i) => <option key={i} value={i}>{l}</option>)}
              </select>
              <small style={{ color: '#9aa6b7', fontSize: 9, marginTop: 3, display: 'block' }}>
                O texto da célula deve ser exatamente o nome de um setor já cadastrado no evento. Se algum nome não existir, a importação é bloqueada por completo.
              </small>
            </label>
            <label>Setor padrão
              <select value={defaultSectorId} onChange={e => setDefaultSectorId(e.target.value)} disabled={importing || sectors.length === 0}>
                <option value="">— nenhum —</option>
                {sectors.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              <small style={{ color: '#9aa6b7', fontSize: 9, marginTop: 3, display: 'block' }}>
                {sectors.length === 0 ? 'Nenhum setor cadastrado neste evento ainda.' : 'Aplicado quando a coluna de setor estiver vazia, ou a todas as linhas se nenhuma coluna for informada.'}
              </small>
            </label>
          </div>
          <button className="primary-button form-submit" type="submit" disabled={importing}>
            {importing ? <><span className="spinner" style={{ width: 12, height: 12, marginRight: 8 }} />Importando {preview.totalRows} linha(s)...</> : `Importar ${preview.totalRows} linha(s)`}
          </button>
        </form>
      </section>
    )}

    {/* Resultado */}
    {result && (
      <section className="panel full-panel">
        <div className="panel-heading"><div><p className="panel-kicker">RESULTADO</p><h2>Importação concluída</h2></div></div>
        <div className="import-result-grid">
          <div className={`import-stat inserted`}><strong>{result.inserted}</strong><span>Inseridos</span></div>
          <div className={`import-stat updated`}><strong>{result.updated}</strong><span>Atualizados</span></div>
          <div className={`import-stat skipped`}><strong>{result.skipped}</strong><span>Ignorados</span></div>
          <div className={`import-stat ${result.errors > 0 ? 'errored' : 'ok'}`}><strong>{result.errors}</strong><span>Erros</span></div>
        </div>
        <p style={{ padding: '0 24px 20px', color: '#8d99ab', fontSize: 11 }}>
          Status: <strong>{result.status}</strong> · Arquivo: {result.fileName}
        </p>
      </section>
    )}

    {/* Histórico */}
    <section className="panel full-panel">
      <div className="panel-heading"><div><p className="panel-kicker">HISTÓRICO</p><h2>Importações anteriores</h2></div></div>
      {loadingHistory
        ? <div className="table-loading"><span className="spinner" />Carregando...</div>
        : history.length === 0
          ? <EmptyState message="Nenhuma importação registrada para este evento." />
          : <div className="table-scroll"><table>
              <thead><tr><th>Arquivo</th><th>Modo</th><th>Total</th><th>Inseridos</th><th>Atualizados</th><th>Ignorados</th><th>Erros</th><th>Status</th><th>Data</th></tr></thead>
              <tbody>{history.map(h => (
                <tr key={h.id}>
                  <td><strong>{h.fileName}</strong><small className="table-id">{h.id}</small></td>
                  <td><small>{h.mode}</small></td>
                  <td>{h.totalRows}</td>
                  <td style={{ color: '#20ba83' }}>{h.inserted}</td>
                  <td style={{ color: '#5272de' }}>{h.updated}</td>
                  <td style={{ color: '#9aa6b7' }}>{h.skipped}</td>
                  <td style={{ color: h.errors > 0 ? '#e66c7d' : '#9aa6b7' }}>{h.errors}</td>
                  <td><StatusBadge value={h.status === 'done' ? 'Approved' : h.status === 'failed' ? 'Rejected' : 'neutral'} /></td>
                  <td>{formatDate(h.createdAt, true)}</td>
                </tr>
              ))}</tbody>
            </table></div>}
    </section>
  </>;
}

export default App;






