# Contrato genérico de callback

O callback deve enviar `event_type` e `payload`. O conteúdo de `payload` é
preservado como JSON para aceitar dados específicos de equipamentos diferentes.

```json
{
  "event_type": "motion_detected",
  "payload": {
    "source": ["equipamento-x"],
    "source_id": "equipment-x_id",
    "data": {
      "evento_original": "movimento",
      "dados_do_fabricante": {}
    },
    "created_at": "2026-08-13T12:00:00Z",
    "created_by": "user_id",
    "deleted_at": "2026-08-13T12:00:00Z",
    "deleted_by": "user_id"
  }
}
```

Campos obrigatórios definidos neste contrato: `event_type`, `payload`,
`payload.source`, `payload.source_id`, `payload.data`, `payload.created_at`,
`payload.created_by`, `payload.deleted_at` e `payload.deleted_by`.
