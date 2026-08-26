{{- /*
Expand the name of the chart.
*/ -}}
{{- define "employee-monitoring.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- /*
Create a default fully qualified app name.
*/ -}}
{{- define "employee-monitoring.fullname" -}}
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

{{- /*
Create chart name and version as used by the chart label.
*/ -}}
{{- define "employee-monitoring.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- /*
Common labels
*/ -}}
{{- define "employee-monitoring.labels" -}}
helm.sh/chart: {{ include "employee-monitoring.chart" . }}
{{ include "employee-monitoring.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- /*
Selector labels
*/ -}}
{{- define "employee-monitoring.selectorLabels" -}}
app.kubernetes.io/name: {{ include "employee-monitoring.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- /*
Create the name of the service account to use
*/ -}}
{{- define "employee-monitoring.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "employee-monitoring.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}