<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { FileJson, Globe, ShieldAlert } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

interface AuditLogDto {
  id: string
  userId?: string
  userName?: string
  ipAddress?: string
  action: string
  entityType?: string
  endpointPath: string
  httpMethod: string
  newValues?: string
  oldValues?: string
  diff?: string
  timestamp: string
  exception?: string
}

defineProps<{
  open: boolean
  selectedLog: AuditLogDto | null
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const { t } = useI18n()

function formatJson(json?: string) {
  if (!json) return ''
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  }
  catch {
    return json
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[700px] max-h-[85vh] flex flex-col p-0">
      <DialogHeader class="p-6 pb-0">
        <DialogTitle class="flex items-center gap-2 text-xl">
          <FileJson class="size-5 text-primary" />
          {{ t('admin.auditLogs.detailDialogTitle') }}
        </DialogTitle>
        <DialogDescription>{{ t('admin.auditLogs.detailDialogDescription') }}</DialogDescription>
      </DialogHeader>

      <div v-if="selectedLog" class="flex-1 overflow-y-auto p-6 pt-4 space-y-6">
        <div class="grid grid-cols-2 gap-4 sm:grid-cols-3">
          <Card class="p-0">
            <CardContent class="space-y-1 p-3">
              <span class="block">{{ t('admin.auditLogs.user') }}</span>
              <p class="text-sm font-medium">{{ selectedLog.userName || t('admin.auditLogs.anonymous') }}</p>
            </CardContent>
          </Card>
          <Card class="p-0">
            <CardContent class="space-y-1 p-3">
              <span class="block">{{ t('admin.auditLogs.action') }}</span>
              <Badge variant="outline" class="mt-0.5 font-mono">{{ selectedLog.action }}</Badge>
            </CardContent>
          </Card>
          <Card class="p-0">
            <CardContent class="space-y-1 p-3">
              <span class="block">{{ t('admin.auditLogs.ip') }}</span>
              <div class="mt-0.5 flex items-center gap-1.5">
                <Globe class="size-3 text-muted-foreground" />
                <p class="text-sm font-mono">{{ selectedLog.ipAddress || '-' }}</p>
              </div>
            </CardContent>
          </Card>
        </div>

        <Card class="p-0 overflow-hidden">
          <div class="flex items-center justify-between border-b bg-muted/50 px-4 py-2">
            <span class="text-xs font-bold uppercase tracking-wider">{{ t('admin.auditLogs.endpointDetails') }}</span>
            <Badge :variant="selectedLog.httpMethod === 'POST' || selectedLog.httpMethod === 'PUT' ? 'default' : 'secondary'">
              {{ selectedLog.httpMethod }}
            </Badge>
          </div>
          <div class="bg-muted/20 p-4">
            <code class="break-all font-mono font-bold text-primary text-xs">{{ selectedLog.endpointPath }}</code>
          </div>
        </Card>

        <div v-if="selectedLog.exception" class="space-y-2 rounded-lg border border-destructive/20 bg-destructive/10 p-4">
          <div class="flex items-center gap-2 text-destructive">
            <ShieldAlert class="size-4" />
            <span class="text-xs font-bold uppercase tracking-wider">{{ t('admin.auditLogs.exceptionLogged') }}</span>
          </div>
          <pre class="whitespace-pre-wrap break-all font-mono text-[10px] opacity-80">{{ selectedLog.exception }}</pre>
        </div>

        <div class="space-y-4">
          <div v-if="selectedLog.diff" class="space-y-2">
            <div class="flex items-center justify-between px-1">
              <span class="text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ t('admin.auditLogs.diff') }}</span>
            </div>
            <pre class="max-h-60 overflow-auto rounded-xl border bg-zinc-950 p-4 font-mono text-[11px] text-emerald-400 shadow-inner">{{ formatJson(selectedLog.diff) }}</pre>
          </div>

          <div v-if="selectedLog.newValues" class="space-y-2">
            <span class="px-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ t('admin.auditLogs.newValues') }}</span>
            <pre class="max-h-60 overflow-auto rounded-xl border bg-zinc-950 p-4 font-mono text-[11px] text-blue-400 shadow-inner">{{ formatJson(selectedLog.newValues) }}</pre>
          </div>

          <div v-if="selectedLog.oldValues" class="space-y-2">
            <span class="px-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ t('admin.auditLogs.oldValues') }}</span>
            <pre class="max-h-60 overflow-auto rounded-xl border bg-zinc-950 p-4 font-mono text-[11px] text-rose-400 shadow-inner">{{ formatJson(selectedLog.oldValues) }}</pre>
          </div>
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>