-- Migration: Corrigir tabela de templates de mensagens de catraca
-- Data: 2026-09-12
-- Descrição: 
--   Remove e recria a tabela fp_turnstile_message_templates com a estrutura correta
--   (problema anterior: coluna id AUTO_INCREMENT extra causando column count mismatch)

DROP TABLE IF EXISTS fp_turnstile_message_templates;

CREATE TABLE fp_turnstile_message_templates (
  template_id INT PRIMARY KEY COMMENT 'ID do template (00-30) para Neon 1.3 getconfig',
  line1 VARCHAR(16) NOT NULL COMMENT 'Primeira linha do display (máx 16 chars)',
  line2 VARCHAR(16) NOT NULL COMMENT 'Segunda linha do display (máx 16 chars)',
  active TINYINT(1) DEFAULT 1 COMMENT 'Ativo/Inativo',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  
  INDEX idx_active (active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Templates de mensagens para catracas (Neon 1.3 - display monochrome 2 linhas × 16 chars)';

INSERT INTO fp_turnstile_message_templates (template_id, line1, line2, active) VALUES
(0,  'ACESSO',      'LIBERADO'),
(1,  'ACESSO',      'NEGADO'),
(2,  'LIBERADA',    'ENTRE'),
(3,  'LIBERADA',    'SAIDA'),
(4,  'BLOQUEADA',   'CADEADO'),
(5,  'PASSE SEU',   'INGRESSO'),
(6,  'INGRESSO',    'INVALIDO'),
(7,  'INGRESSO',    'EXPIRADO'),
(8,  'LIMITE',      'ATINGIDO'),
(9,  'ERRO',        'REDE'),
(10, 'AGUARDE',     'VALIDACAO'),
(11, 'BEM-VINDO',   'EVENTO'),
(12, 'PROIBIDO',    'ACESSO'),
(13, 'VISITANTE',   'NAO PERMIT'),
(14, 'SETOR',       'BLOQUEADO'),
(15, 'HORARIO',     'FECHADO'),
(16, 'TENTE',       'NOVAMENTE'),
(17, 'CARTAO',      'LIDO'),
(18, 'QR CODE',     'LIDO'),
(19, 'BIOMETRIA',   'OK'),
(20, 'DUPLICADO',   'ENTRADA'),
(21, 'AGUARDE',     'OPERADOR'),
(22, 'VALIDADO',    'OPERADOR'),
(23, 'NEGADO',      'OPERADOR'),
(24, 'MODO',        'MANUTENCAO'),
(25, 'SINCRONIZAR', 'DADOS'),
(26, 'OFFLINE',     'USAR LOCAL'),
(27, 'ONLINE',      'RESTAURADO'),
(28, 'CONFIGURAR',  'CATRACA'),
(29, 'TESTE',       'CONEXAO'),
(30, 'CUSTOMIZADO', 'ID30');
