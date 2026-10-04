<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdWebhooksPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdWebhooksPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdWebhooksPageViewState }>()
const {
  Webhook, Plus, Copy, RotateCw, Send, Trash2,
  targets, mayManage, loading, error, page, pageCount, total, pageLimit,
  loadPage, setPageSize,
  formOpen, editingId, form, saving, pendingId, deletingTarget,
  signingSecret, secretOpen, testStates, createTarget, editTarget,
  setFormOpen, save, setEnabled, requestDelete, setDeleteOpen,
  rotate, copySecret, remove, test,
  deliveries, deliveriesLoading, deliveriesError, deliveriesPage,
  deliveriesPageCount, deliveriesTotal, deliveriesPageLimit,
  loadDeliveriesPage, setDeliveriesPageSize, refreshDeliveries,
  deliveryStateLabel, deliveryStateVariant, payloadStateLabel,
  formatSeconds, adminFormatDateTime,
} = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="gap-0">
      <CardHeader class="flex-row items-start justify-between gap-4">
        <div class="flex flex-col gap-1.5">
          <CardTitle class="flex items-center gap-2">
            <component :is="Webhook" class="size-5 text-primary" />
            {{ $t('webhook.title') }}
          </CardTitle>
          <CardDescription>{{ $t('webhook.description') }}</CardDescription>
        </div>
        <Button v-if="mayManage" @click="createTarget">
          <component :is="Plus" data-icon="inline-start" />
          {{ $t('webhook.add') }}
        </Button>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Alert>
          <AlertTitle>{{ $t('webhook.deliveryModel') }}</AlertTitle>
          <AlertDescription>{{ $t('webhook.deliveryModelDescription') }}</AlertDescription>
        </Alert>

        <Skeleton v-if="loading" class="h-32 w-full" />
        <Alert v-else-if="error" variant="destructive">
          <AlertTitle>{{ $t('webhook.loadFailed') }}</AlertTitle>
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>
        <Empty v-else-if="targets.length === 0">
          <EmptyHeader>
            <EmptyMedia variant="icon"><component :is="Webhook" /></EmptyMedia>
            <EmptyTitle>{{ $t('webhook.empty') }}</EmptyTitle>
            <EmptyDescription>{{ $t('webhook.emptyDescription') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>
        <div v-else class="flex flex-col gap-3">
          <Card v-for="target in targets" :key="target.id ?? undefined" class="gap-4 bg-card/45">
            <CardHeader class="flex-row items-start justify-between gap-4">
              <div class="min-w-0">
                <CardTitle class="truncate text-base">{{ target.name }}</CardTitle>
                <CardDescription class="truncate font-mono text-xs">{{ target.endpointUrl ?? target.endpointHost }}</CardDescription>
              </div>
              <div class="flex items-center gap-2">
                <Badge :variant="target.enabled ? 'default' : 'secondary'">
                  {{ target.enabled ? $t('webhook.enabled') : $t('webhook.disabled') }}
                </Badge>
                <Switch
                  v-if="mayManage"
                  :model-value="target.enabled"
                  :disabled="pendingId === target.id"
                  @update:model-value="setEnabled(target, $event)"
                />
              </div>
            </CardHeader>
            <CardContent class="flex flex-wrap items-center gap-2">
              <Button v-if="mayManage" variant="secondary" size="sm" @click="editTarget(target)">
                {{ $t('administration.label.edit') }}
              </Button>
              <Button
                v-if="mayManage"
                variant="secondary"
                size="sm"
                :disabled="pendingId === target.id"
                @click="test(target)"
              >
                <Spinner v-if="testStates[target.id!] === 'Pending'" data-icon="inline-start" />
                <component v-else :is="Send" data-icon="inline-start" />
                {{ $t('webhook.test') }}
              </Button>
              <Button
                v-if="mayManage"
                variant="secondary"
                size="sm"
                :disabled="pendingId === target.id"
                @click="rotate(target)"
              >
                <component :is="RotateCw" data-icon="inline-start" />
                {{ $t('webhook.rotate') }}
              </Button>
              <Button
                v-if="mayManage"
                variant="destructive"
                size="sm"
                @click="requestDelete(target)"
              >
                <component :is="Trash2" data-icon="inline-start" />
                {{ $t('common.action.delete') }}
              </Button>
              <Badge v-if="testStates[target.id!] === 'Succeeded'" variant="outline">{{ $t('webhook.testSucceeded') }}</Badge>
              <Badge v-else-if="testStates[target.id!] === 'Failed'" variant="destructive">{{ $t('webhook.testFailed') }}</Badge>
            </CardContent>
          </Card>
          <OffsetPagination
            :page="page"
            :page-count="pageCount"
            :total="total"
            :limit="pageLimit"
            :loading="loading"
            @update:page="loadPage"
            @update:limit="setPageSize"
          />
        </div>
      </CardContent>
    </Card>

    <Card class="gap-0">
      <CardHeader class="flex-row items-start justify-between gap-4">
        <CardTitle>{{ $t('webhook.diagnosticsTitle') }}</CardTitle>
        <Button variant="secondary" size="sm" :disabled="deliveriesLoading" @click="refreshDeliveries">
          <component :is="RotateCw" data-icon="inline-start" />
          {{ $t('webhook.refreshDiagnostics') }}
        </Button>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Skeleton v-if="deliveriesLoading && !deliveries.length" class="h-36 w-full" />
        <Alert v-else-if="deliveriesError" variant="destructive">
          <AlertTitle>{{ $t('webhook.diagnosticsLoadFailed') }}</AlertTitle>
          <AlertDescription>{{ deliveriesError }}</AlertDescription>
        </Alert>
        <Empty v-else-if="!deliveries.length">
          <EmptyHeader>
            <EmptyTitle>{{ $t('webhook.diagnosticsEmpty') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <template v-else>
          <div v-for="(delivery, index) in deliveries" :key="`${delivery.eventId}:${delivery.targetId}`" class="flex flex-col gap-3">
            <Separator v-if="index" />
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div class="min-w-0">
                <p class="break-all font-mono text-sm font-medium">{{ delivery.eventType }}</p>
                <p class="break-all font-mono text-xs text-muted-foreground">{{ $t('webhook.eventId') }}: {{ delivery.eventId }}</p>
                <p class="break-all font-mono text-xs text-muted-foreground">{{ $t('webhook.targetId') }}: {{ delivery.targetId }}</p>
              </div>
              <div class="flex flex-wrap items-center gap-2">
                <Badge :variant="deliveryStateVariant(delivery.state)">{{ deliveryStateLabel(delivery.state) }}</Badge>
                <Badge variant="outline">{{ payloadStateLabel(delivery.payloadState) }}</Badge>
              </div>
            </div>
            <dl class="grid gap-x-6 gap-y-3 text-xs sm:grid-cols-2 xl:grid-cols-4">
              <div><dt class="text-muted-foreground">{{ $t('webhook.domainEventTime') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.domainEventCreatedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.outboxTime') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.outboxPersistedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.workerDequeuedAt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.workerDequeuedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.projectionReadyAt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.publicProjectionReadyAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.capturedAt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.capturedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.queueAge') }}</dt><dd class="font-mono tabular-nums">{{ formatSeconds(delivery.queueAgeSeconds) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.projectionWait') }}</dt><dd class="font-mono tabular-nums">{{ formatSeconds(delivery.projectionWaitSeconds) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.firstHttpAttempt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.firstHttpAttemptStartedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.lastHttpAttempt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.lastHttpAttemptStartedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.lastHttpCompleted') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.lastHttpAttemptCompletedAt) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.httpDuration') }}</dt><dd class="font-mono tabular-nums">{{ formatSeconds(delivery.lastHttpAttemptDurationSeconds) }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.httpStatus') }}</dt><dd class="font-mono tabular-nums">{{ delivery.lastHttpStatusCode ?? '—' }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.retryCounts') }}</dt><dd class="font-mono tabular-nums">{{ delivery.projectionRetryCount ?? 0 }} / {{ delivery.httpRetryCount ?? 0 }}</dd></div>
              <div><dt class="text-muted-foreground">{{ $t('webhook.nextRetryAt') }}</dt><dd class="font-mono tabular-nums">{{ adminFormatDateTime(delivery.nextRetryAt) }}</dd></div>
              <div v-if="delivery.deadLetterReason"><dt class="text-muted-foreground">{{ $t('webhook.deadLetterReason') }}</dt><dd class="font-mono text-destructive">{{ delivery.deadLetterReason }}</dd></div>
            </dl>
          </div>
          <OffsetPagination
            :page="deliveriesPage" :page-count="deliveriesPageCount"
            :total="deliveriesTotal" :limit="deliveriesPageLimit"
            :loading="deliveriesLoading"
            @update:page="loadDeliveriesPage"
            @update:limit="setDeliveriesPageSize"
          />
        </template>
      </CardContent>
    </Card>

    <Dialog :open="formOpen" @update:open="setFormOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ editingId ? $t('webhook.edit') : $t('webhook.add') }}</DialogTitle>
          <DialogDescription>{{ $t('webhook.formDescription') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="webhook-name">{{ $t('webhook.name') }}</FieldLabel>
            <Input id="webhook-name" v-model="form.name" maxlength="100" />
          </Field>
          <Field>
            <FieldLabel for="webhook-url">{{ $t('webhook.endpointUrl') }}</FieldLabel>
            <Input id="webhook-url" v-model="form.endpointUrl" type="url" :placeholder="$t('webhook.endpointPlaceholder')" />
            <FieldDescription>{{ $t('webhook.endpointDescription') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Switch id="webhook-enabled" v-model="form.enabled" />
            <FieldLabel for="webhook-enabled">{{ $t('webhook.enableImmediately') }}</FieldLabel>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="secondary" :disabled="saving" @click="setFormOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="saving" @click="save">
            <Spinner v-if="saving" data-icon="inline-start" />
            {{ $t('common.action.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="secretOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('webhook.secretTitle') }}</DialogTitle>
          <DialogDescription>{{ $t('webhook.secretDescription') }}</DialogDescription>
        </DialogHeader>
        <div class="flex items-center gap-2">
          <Input :model-value="signingSecret ?? ''" readonly class="font-mono" />
          <Button size="icon" variant="secondary" :aria-label="$t('webhook.copySecret')" @click="copySecret">
            <component :is="Copy" />
          </Button>
        </div>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="Boolean(deletingTarget)" @update:open="setDeleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('webhook.deleteTitle') }}</AlertDialogTitle>
          <AlertDialogDescription>{{ $t('webhook.deleteDescription', { name: deletingTarget?.name ?? '' }) }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <AlertDialogAction :disabled="Boolean(pendingId)" @click="remove">
            <Spinner v-if="pendingId" data-icon="inline-start" />
            {{ $t('common.action.delete') }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
