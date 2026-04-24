{{/*
Expand the name of the chart.
*/}}
{{- define "cena.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "cena.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "cena.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "cena.labels" -}}
helm.sh/chart: {{ include "cena.chart" . }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: cena-platform
{{- end }}

{{/*
Selector labels for a specific component.
Usage: {{ include "cena.selectorLabels" (dict "root" . "component" "student") }}
*/}}
{{- define "cena.selectorLabels" -}}
app.kubernetes.io/name: {{ include "cena.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{/*
Full labels for a specific component (common + selector + version).
Usage: {{ include "cena.componentLabels" (dict "root" . "component" "student") }}
*/}}
{{- define "cena.componentLabels" -}}
{{ include "cena.labels" .root }}
{{ include "cena.selectorLabels" (dict "root" .root "component" .component) }}
app.kubernetes.io/version: {{ .root.Chart.AppVersion | quote }}
{{- end }}

{{/*
Service account name
*/}}
{{- define "cena.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "cena.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Image pull secrets
*/}}
{{- define "cena.imagePullSecrets" -}}
{{- if .Values.imagePullSecrets }}
imagePullSecrets:
{{- range .Values.imagePullSecrets }}
  - name: {{ .name }}
{{- end }}
{{- end }}
{{- end }}
