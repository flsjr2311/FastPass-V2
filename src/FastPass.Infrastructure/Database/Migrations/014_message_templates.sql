-- Migration 014: mensagens de validação passam a ter um padrão GLOBAL.
-- fp_access_message_templates guarda os 14 templates compartilhados por TODOS os eventos.
-- fp_access_messages passa a guardar somente os OVERRIDES por evento (uma linha só existe
-- se alguém customizou aquele código especificamente para aquele evento).

CREATE TABLE IF NOT EXISTS fp_access_message_templates (
    id CHAR(36) NOT NULL,
    code VARCHAR(80) NOT NULL,
    title VARCHAR(120) NOT NULL,
    message VARCHAR(500) NOT NULL,
    background_start CHAR(7) NOT NULL DEFAULT '#EB3349',
    background_end CHAR(7) NOT NULL DEFAULT '#F45C43',
    title_color CHAR(7) NOT NULL DEFAULT '#FFFFFF',
    message_color CHAR(7) NOT NULL DEFAULT '#FFFFFF',
    title_size VARCHAR(20) NOT NULL DEFAULT '28dp',
    message_size VARCHAR(20) NOT NULL DEFAULT '18dp',
    title_bold TINYINT(1) NOT NULL DEFAULT 1,
    message_bold TINYINT(1) NOT NULL DEFAULT 1,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_access_message_templates_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO fp_access_message_templates
    (id, code, title, message, background_start, background_end, title_color, message_color, title_size, message_size, title_bold, message_bold, active, created_at, updated_at)
VALUES
    (UUID(), 'ACESSO_CONCEDIDO', 'Acesso liberado', 'Acesso liberado com sucesso.', '#11998E', '#38EF7D', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'ACESSO_NEGADO', 'Acesso negado', 'Acesso não autorizado.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'CRACHA_INATIVO', 'Acesso negado', 'Crachá ou funcionário inativo.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'CRACHA_AINDA_NAO_VALIDO', 'Acesso negado', 'Crachá ainda não está válido.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'CRACHA_EXPIRADO', 'Acesso negado', 'Crachá expirado.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'CRACHA_SEM_AUTORIZACAO', 'Acesso negado', 'Crachá sem autorização para este evento, portaria, setor ou direção.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'INGRESSO_NAO_ENCONTRADO', 'Acesso negado', 'Ingresso não encontrado.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'INGRESSO_INATIVO', 'Acesso negado', 'Ingresso inativo.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'MATRIZ_NAO_AUTORIZADA', 'Acesso negado', 'Ingresso sem associação ativa entre portaria, setor e direção.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'INGRESSO_SEM_AUTORIZACAO', 'Acesso negado', 'Ingresso sem autorização para este evento, portaria, setor ou direção.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'LIMITE_ENTRADAS_ATINGIDO', 'Acesso negado', 'Limite de entradas do ingresso atingido.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'PRESENCA_NAO_REGISTRADA', 'Acesso negado', 'Não há presença registrada para este ingresso.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'LIMITE_ENTRADAS_CONCORRENTE', 'Acesso negado', 'Ingresso atingiu o limite de entradas durante a validação.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    (UUID(), 'SAIDA_SEM_PRESENCA', 'Acesso negado', 'Não há presença registrada para este ingresso durante a saída.', '#EB3349', '#F45C43', '#FFFFFF', '#FFFFFF', '28dp', '18dp', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE code = code;

-- Remove as cópias duplicadas por evento que existiam antes: qualquer mensagem que
-- ainda esteja igual ao template padrão (nunca foi customizada) deixa de ser um
-- registro físico — a resolução passa a herdar do template automaticamente.
-- Mantemos apenas overrides: linhas cujo conteúdo diverge do template correspondente.
DELETE m FROM fp_access_messages m
INNER JOIN fp_access_message_templates t ON t.code = m.code
WHERE m.title = t.title
  AND m.message = t.message
  AND m.background_start = t.background_start
  AND m.background_end = t.background_end
  AND m.title_color = t.title_color
  AND m.message_color = t.message_color
  AND m.title_size = t.title_size
  AND m.message_size = t.message_size
  AND m.title_bold = t.title_bold
  AND m.message_bold = t.message_bold
  AND m.active = 1;
