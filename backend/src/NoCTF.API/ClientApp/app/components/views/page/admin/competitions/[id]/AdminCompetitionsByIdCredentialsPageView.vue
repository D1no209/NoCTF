<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdCredentialsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdCredentialsPage'

const props = defineProps<{ state: AdminCompetitionsByIdCredentialsPageViewState }>()
const {
  canJudge, selectedFile, chooseFile, importPending, importError, importCredentials,
  batch, batchId, batches, loadPending, loadBatch, rotatePending, rotateBatch, rotateUser,
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
            <FileUpload id="credential-import-file" accept=".csv,.xlsx" @change="chooseFile" />
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
          <Field v-if="batches.length">
            <FieldLabel for="credential-batch-id">{{ $t('administration.credentials.batchId') }}</FieldLabel>
            <Select v-model="batchId">
              <SelectTrigger id="credential-batch-id" class="w-80"><SelectValue /></SelectTrigger>
              <SelectContent><SelectItem v-for="item in batches" :key="item.batchId" :value="item.batchId!">{{ item.sourceFileName }} · {{ item.batchId }}</SelectItem></SelectContent>
            </Select>
          </Field>
          <Field v-else>
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
        <ScrollSurface v-if="batch?.rows?.length" axis="x" class="max-w-full rounded-lg border">
          <Table class="min-w-[900px]">
            <TableHeader><TableRow><TableHead>{{ $t('administration.credentials.username') }}</TableHead><TableHead>{{ $t('administration.credentials.email') }}</TableHead><TableHead>{{ $t('administration.credentials.team') }}</TableHead><TableHead>{{ $t('administration.credentials.captain') }}</TableHead><TableHead>{{ $t('administration.credentials.password') }}</TableHead><TableHead class="text-right" /></TableRow></TableHeader>
            <TableBody>
              <TableRow v-for="row in batch.rows" :key="`${row.userId}-${row.teamName}`">
                <TableCell class="font-mono">{{ row.userName }}</TableCell><TableCell>{{ row.email || '—' }}</TableCell><TableCell>{{ row.teamName }}</TableCell><TableCell>{{ row.captain ? $t('administration.credentials.captainYes') : $t('administration.credentials.captainNo') }}</TableCell><TableCell class="font-mono">{{ row.password }}</TableCell><TableCell class="text-right"><Button size="sm" variant="ghost" :disabled="rotatePending" @click="rotateUser(row.userId)">{{ $t('administration.credentials.rotateUser') }}</Button></TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </ScrollSurface>
        <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('administration.credentials.empty') }}</EmptyTitle></EmptyHeader></Empty>
      </CardContent>
    </Card>
  </div>
</template>
