{{- define "reapseek.labels" -}}
app.kubernetes.io/name: {{ . }}
app.kubernetes.io/part-of: reapseek
{{- end }}

{{- define "reapseek.selector" -}}
app.kubernetes.io/name: {{ . }}
{{- end }}

{{- define "reapseek.secret" -}}
- name: {{ .name }}
  valueFrom:
    secretKeyRef:
      name: {{ .root.Values.secretName }}
      key: {{ .key }}
      {{- if .optional }}
      optional: true
      {{- end }}
{{- end }}

{{- define "reapseek.dbEnv" -}}
{{ include "reapseek.secret" (dict "root" . "name" "POSTGRES_PASSWORD" "key" "POSTGRES_PASSWORD") }}
- name: ConnectionStrings__Postgres
  value: Host=postgres;Database=reapseek;Username=reapseek;Password=$(POSTGRES_PASSWORD)
{{- end }}

{{- define "reapseek.otelEnv" -}}
{{- if .Values.observability.enabled }}
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: http://lgtm:4317
- name: OTEL_METRIC_EXPORT_INTERVAL
  value: "10000"
{{- end }}
{{- end }}

{{- define "reapseek.publicUrl" -}}
http://{{ .host }}:{{ .root.Values.publicPort }}
{{- end }}

{{- define "reapseek.apiEnv" -}}
{{ include "reapseek.dbEnv" . }}
{{ include "reapseek.secret" (dict "root" . "name" "REDIS_PASSWORD" "key" "REDIS_PASSWORD") }}
- name: ConnectionStrings__Redis
  value: redis:6379,password=$(REDIS_PASSWORD)
{{ include "reapseek.otelEnv" . }}
{{ include "reapseek.secret" (dict "root" . "name" "Auth__BootstrapEmail" "key" "ADMIN_EMAIL") }}
{{ include "reapseek.secret" (dict "root" . "name" "Auth__BootstrapPassword" "key" "ADMIN_PASSWORD") }}
- name: Auth__SecureCookie
  value: "false"
- name: Auth__AttemptsPerMinute
  value: "20"
- name: ForwardedHeaders__TrustedNetworks
  value: 10.42.0.0/16
- name: OpenSearch__Url
  value: http://opensearch:9200
- name: Media__UploadPath
  value: /data/uploads
- name: Storefront__PublicUrl
  value: {{ include "reapseek.publicUrl" (dict "root" . "host" .Values.hosts.storefront) }}
- name: Backoffice__PublicUrl
  value: {{ include "reapseek.publicUrl" (dict "root" . "host" .Values.hosts.backoffice) }}
- name: Payments__Sandbox__ApiUrl
  value: http://sandbox:8080
{{ include "reapseek.secret" (dict "root" . "name" "Payments__Sandbox__SecretKey" "key" "SANDBOX_SECRET_KEY") }}
{{ include "reapseek.secret" (dict "root" . "name" "Payments__Sandbox__WebhookSecret" "key" "SANDBOX_WEBHOOK_SECRET") }}
- name: Payments__WebhookUrl
  value: http://api:8080/api/v1/payments/sandbox/webhook
- name: Shipping__WebhookUrl
  value: http://api:8080/api/v1/shipping/sandbox/webhook
- name: ReapDeep__RecommendUrl
  value: {{ .Values.reapdeep.recommendUrl }}
- name: ReapDeep__ApiUrl
  value: {{ .Values.reapdeep.apiUrl }}
{{ include "reapseek.secret" (dict "root" . "name" "ReapDeep__PublicKey" "key" "REAPDEEP_PUBLIC_KEY" "optional" true) }}
{{ include "reapseek.secret" (dict "root" . "name" "ReapDeep__RecommendationSecret" "key" "REAPDEEP_RECOMMENDATION_SECRET" "optional" true) }}
{{ include "reapseek.secret" (dict "root" . "name" "ReapDeep__ReadKey" "key" "REAPDEEP_READ_KEY" "optional" true) }}
- name: Recommendations__CacheSeconds
  value: "30"
{{- end }}

{{- define "reapseek.securityContext" -}}
securityContext:
  allowPrivilegeEscalation: false
  capabilities:
    drop: ["ALL"]
{{- end }}
