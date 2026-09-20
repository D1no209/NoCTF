<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdWebhooksPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdWebhooksPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdWebhooksPageViewState }>()
const {
  Webhook, Plus, Copy, RotateCw, Send, Trash2,
  targets, mayManage, nextCursor, loading, loadingMore, error,
  formOpen, editingId, form, saving, pendingId, deletingTarget,
  signingSecret, secretOpen, testStates, createTarget, editTarget,
  setFormOpen, save, setEnabled, requestDelete, setDeleteOpen, loadMore,
  rotate, copySecret, remove, test,
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
          <Card v-for="target in targets" :key="target.id" class="gap-4 bg-card/45">
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
                {{ $t('ui.edit') }}
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
                {{ $t('ui.delete') }}
              </Button>
              <Badge v-if="testStates[target.id!] === 'Succeeded'" variant="outline">{{ $t('webhook.testSucceeded') }}</Badge>
              <Badge v-else-if="testStates[target.id!] === 'Failed'" variant="destructive">{{ $t('webhook.testFailed') }}</Badge>
            </CardContent>
          </Card>
          <Button v-if="nextCursor" variant="secondary" :disabled="loadingMore" @click="loadMore">
            <Spinner v-if="loadingMore" data-icon="inline-start" />
            {{ $t('ui.loadMore') }}
          </Button>
        </div>
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
          <Button variant="secondary" :disabled="saving" @click="setFormOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="saving" @click="save">
            <Spinner v-if="saving" data-icon="inline-start" />
            {{ $t('ui.save') }}
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
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <AlertDialogAction :disabled="Boolean(pendingId)" @click="remove">
            <Spinner v-if="pendingId" data-icon="inline-start" />
            {{ $t('ui.delete') }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
