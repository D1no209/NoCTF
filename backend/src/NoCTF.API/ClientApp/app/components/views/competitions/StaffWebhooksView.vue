<script setup lang="ts">
import { toRefs } from 'vue'
import type { StaffWebhooksViewState } from '~/features/competitions/staff-webhooks/useStaffWebhooks'
const props = defineProps<{ state: StaffWebhooksViewState }>()
const { page, records, categories, categoryKeys, stateKeys, mayManage, formOpen, editingId, form, pending, signingSecret, secretOpen,
  deleting, recordsTarget, testState, poll, create, edit, setFormOpen, toggleCategory, setSecretOpen, requestDelete, setDeleteOpen,
  reload, save, rotate, remove, test, showRecords, copySecret } = toRefs(props.state)
</script>
<template>
  <Card>
    <CardHeader class="flex-row flex-wrap items-center justify-between gap-3">
      <CardTitle>{{ $t('staffWebhook.title') }}</CardTitle>
      <Button v-if="mayManage" @click="create">{{ $t('webhook.add') }}</Button>
    </CardHeader>
    <CardContent class="flex flex-col gap-4">
      <Alert><AlertTitle>{{ $t('staffWebhook.sharingTitle') }}</AlertTitle><AlertDescription>{{ $t('staffWebhook.sharingNotice') }}</AlertDescription></Alert>
      <Skeleton v-if="page.loading.value" class="h-24 w-full" />
      <Alert v-else-if="page.error.value" variant="destructive"><AlertTitle>{{ $t('webhook.loadFailed') }}</AlertTitle><AlertDescription>{{ $message(page.error.value.displayMessage) }}</AlertDescription><Button variant="secondary" @click="reload">{{ $t('common.label.reload') }}</Button></Alert>
      <Empty v-else-if="!page.items.value.length"><EmptyHeader><EmptyTitle>{{ $t('staffWebhook.empty') }}</EmptyTitle></EmptyHeader></Empty>
      <div v-for="target in page.items.value" :key="target.id" class="flex flex-col gap-3 py-3">
        <div class="flex flex-wrap items-center gap-2"><span class="min-w-0 flex-1 break-words font-semibold">{{ target.name }}</span><Badge>{{ $t(target.enabled ? 'webhook.enabled' : 'webhook.disabled') }}</Badge><Badge v-if="target.synchronizing" variant="secondary">{{ $t('staffWebhook.synchronizing') }}</Badge><Badge v-if="target.authorizationRevoked" variant="destructive">{{ $t('staffWebhook.revoked') }}</Badge></div>
        <span v-if="target.endpointUrl" class="break-all text-sm text-muted-foreground">{{ target.endpointUrl }}</span>
        <div class="flex flex-wrap gap-2"><Badge v-for="category in target.categories" :key="category" variant="secondary">{{ $t(categoryKeys[category]) }}</Badge></div>
        <div class="flex flex-wrap gap-2">
          <Button v-if="mayManage" variant="secondary" :disabled="pending" @click="edit(target)">{{ $t('administration.label.edit') }}</Button>
          <Button v-if="mayManage" variant="secondary" :disabled="pending" @click="rotate(target)">{{ $t('webhook.rotate') }}</Button>
          <Button v-if="mayManage" variant="secondary" :disabled="pending" @click="test(target)">{{ $t('webhook.test') }}</Button>
          <Button variant="secondary" @click="showRecords(target)">{{ $t('staffWebhook.deliveries') }}</Button>
          <Button v-if="mayManage" variant="destructive" :disabled="pending" @click="requestDelete(target)">{{ $t('common.action.delete') }}</Button>
        </div>
      </div>
      <OffsetPagination :page="page.page.value" :total="page.total.value" :page-count="page.pageCount.value" :limit="page.limit.value" :loading="page.loading.value" @update:page="page.loadPage" @update:limit="page.setPageSize" />
      <Alert v-if="testState !== null"><AlertTitle>{{ $t('webhook.test') }}</AlertTitle><AlertDescription>{{ $t(stateKeys[testState]) }}</AlertDescription><AlertDescription v-if="poll.timedOut.value">{{ $t('staffWebhook.testTimedOut') }}</AlertDescription></Alert>
      <template v-if="recordsTarget">
        <Separator /><h3 class="text-lg font-semibold">{{ $t('staffWebhook.deliveries') }} · {{ recordsTarget.name }}</h3>
        <Skeleton v-if="records.loading.value" class="h-20 w-full" />
        <Alert v-else-if="records.error.value" variant="destructive"><AlertDescription>{{ $message(records.error.value.displayMessage) }}</AlertDescription></Alert>
        <Table v-else><TableHeader><TableRow><TableHead>{{ $t('staffWebhook.sequence') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead><TableHead>{{ $t('staffWebhook.attempts') }}</TableHead><TableHead>{{ $t('staffWebhook.httpStatus') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="record in records.items.value" :key="record.eventId"><TableCell class="font-mono">{{ record.sequence }}</TableCell><TableCell>{{ $t(stateKeys[record.state ?? 0]) }}</TableCell><TableCell>{{ record.attempts }}</TableCell><TableCell>{{ record.lastStatusCode ?? '—' }}</TableCell></TableRow></TableBody></Table>
        <OffsetPagination :page="records.page.value" :total="records.total.value" :page-count="records.pageCount.value" :limit="records.limit.value" :loading="records.loading.value" @update:page="records.loadPage" @update:limit="records.setPageSize" />
      </template>
    </CardContent>
  </Card>
  <Dialog :open="formOpen" @update:open="setFormOpen"><DialogContent><DialogHeader><DialogTitle>{{ $t(editingId ? 'staffWebhook.edit' : 'staffWebhook.add') }}</DialogTitle><DialogDescription>{{ $t('staffWebhook.sharingNotice') }}</DialogDescription></DialogHeader>
    <UiForm class="flex flex-col gap-4" @submit="save"><FieldGroup>
      <Field><FieldLabel for="staff-hook-name">{{ $t('webhook.name') }}</FieldLabel><Input id="staff-hook-name" v-model="form.name" maxlength="100" required /></Field>
      <Field><FieldLabel for="staff-hook-url">{{ $t('webhook.endpointUrl') }}</FieldLabel><Input id="staff-hook-url" v-model="form.endpointUrl" type="url" :placeholder="$t('webhook.endpointPlaceholder')" required /></Field>
      <Field v-for="category in categories" :key="category" orientation="horizontal"><Checkbox :id="`staff-hook-${category}`" :model-value="form.categories.includes(category)" @update:model-value="toggleCategory(category, $event)" /><FieldLabel :for="`staff-hook-${category}`">{{ $t(categoryKeys[category]) }}</FieldLabel></Field>
      <Field orientation="horizontal"><Switch id="staff-hook-enabled" v-model="form.enabled" /><FieldLabel for="staff-hook-enabled">{{ $t('webhook.enabled') }}</FieldLabel></Field>
    </FieldGroup><DialogFooter><Button type="submit" :disabled="pending || !form.categories.length"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('common.action.save') }}</Button></DialogFooter></UiForm>
  </DialogContent></Dialog>
  <Dialog :open="secretOpen" @update:open="setSecretOpen"><DialogContent><DialogHeader><DialogTitle>{{ $t('webhook.secretTitle') }}</DialogTitle><DialogDescription>{{ $t('staffWebhook.secretOnce') }}</DialogDescription></DialogHeader><Input :model-value="signingSecret ?? ''" readonly :aria-label="$t('webhook.secretTitle')" /><DialogFooter><Button @click="copySecret">{{ $t('common.action.copy') }}</Button></DialogFooter></DialogContent></Dialog>
  <Dialog :open="deleting !== null" @update:open="setDeleteOpen"><DialogContent><DialogHeader><DialogTitle>{{ $t('staffWebhook.delete') }}</DialogTitle><DialogDescription>{{ $t('staffWebhook.deleteNotice') }}</DialogDescription></DialogHeader><DialogFooter><Button variant="destructive" :disabled="pending" @click="remove"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('common.action.delete') }}</Button></DialogFooter></DialogContent></Dialog>
</template>
