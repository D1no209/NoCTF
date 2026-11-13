<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdCredentialsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdCredentialsPage'

const props = defineProps<{ state: AdminCompetitionsByIdCredentialsPageViewState }>()
const {
  canJudge, selectedFile, chooseFile, importPending, importError, importCredentials,
  batch, batchId, loadPending, loadBatch, rotatePending, rotateBatch, rotateUser,
  downloadPackage, downloadPdf, printPage,
} = toRefs(props.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('administration.credentials.title') }}</CardTitle>
        <CardDescription>{{ $t('administration.credentials.description') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Alert v-if="!canJudge" variant="destructive"><AlertTitle>{{ $t('administration.credentials.forbidden') }}</AlertTitle></Alert>
        <div v-else class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="credential-import-file">{{ $t('administration.credentials.importFile') }}</FieldLabel>
            <Input id="credential-import-file" type="file" accept=".csv,.xlsx" @change="chooseFile" />
            <FieldDescription>{{ $t('administration.credentials.importHint') }}</FieldDescription>
          </Field>
          <Button :disabled="!selectedFile || importPending" @click="importCredentials">
            <Spinner v-if="importPending" data-icon="inline-start" />{{ $t('administration.credentials.import') }}
          </Button>
        </div>
        <Alert v-if="importError" variant="destructive"><AlertTitle>{{ $message(importError) }}</AlertTitle></Alert>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ $t('administration.credentials.batchTitle') }}</CardTitle>
        <CardDescription>{{ $t('administration.credentials.batchDescription') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="credential-batch-id">{{ $t('administration.credentials.batchId') }}</FieldLabel>
            <Input id="credential-batch-id" v-model="batchId" class="w-80 font-mono" />
          </Field>
          <Button variant="outline" :disabled="loadPending || !batchId" @click="loadBatch">
            <Spinner v-if="loadPending" data-icon="inline-start" />{{ $t('administration.credentials.load') }}
          </Button>
          <Button variant="outline" :disabled="!batch?.batchId" @click="downloadPackage">{{ $t('administration.credentials.downloadPackage') }}</Button>
          <Button variant="outline" :disabled="!batch?.batchId" @click="downloadPdf">{{ $t('administration.credentials.downloadPdf') }}</Button>
          <Button variant="outline" :disabled="!batch?.batchId" @click="printPage">{{ $t('administration.credentials.print') }}</Button>
          <Button variant="destructive" :disabled="rotatePending || !batch?.batchId" @click="rotateBatch">
            <Spinner v-if="rotatePending" data-icon="inline-start" />{{ $t('administration.credentials.rotateBatch') }}
          </Button>
        </div>
        <div v-if="batch?.rows?.length" class="overflow-x-auto">
          <table class="w-full min-w-[900px] text-sm">
            <thead><tr class="border-b text-left"><th class="p-2">{{ $t('administration.credentials.username') }}</th><th class="p-2">{{ $t('administration.credentials.email') }}</th><th class="p-2">{{ $t('administration.credentials.team') }}</th><th class="p-2">{{ $t('administration.credentials.captain') }}</th><th class="p-2">{{ $t('administration.credentials.password') }}</th><th class="p-2" /></tr></thead>
            <tbody>
              <tr v-for="row in batch.rows" :key="`${row.userName}-${row.teamName}`" class="border-b">
                <td class="p-2 font-mono">{{ row.userName }}</td><td class="p-2">{{ row.email || '—' }}</td><td class="p-2">{{ row.teamName }}</td><td class="p-2">{{ row.captain ? $t('administration.credentials.captainYes') : $t('administration.credentials.captainNo') }}</td><td class="p-2 font-mono">{{ row.password }}</td><td class="p-2 text-right"><Button size="sm" variant="ghost" :disabled="rotatePending" @click="rotateUser(row.userId)">{{ $t('administration.credentials.rotateUser') }}</Button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('administration.credentials.empty') }}</EmptyTitle></EmptyHeader></Empty>
      </CardContent>
    </Card>
  </div>
</template>
