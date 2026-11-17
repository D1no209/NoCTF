<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdCredentialsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdCredentialsPage'

const props = defineProps<{ state: AdminCompetitionsByIdCredentialsPageViewState }>()
const {
  canJudge, selectedFile, chooseFile, importPending, importError, importIssues, importCredentials,
  batch, batchId, batches, loadPending, loadBatch, rotatePending, requestBatchRotation, requestUserRotation,
  downloadPackage, downloadPdf, downloadTemplate, busy, confirmRotation, applyRotation,
  page, pageCount, pageLimit, total, rows, loadPage, setPageSize,
} = toRefs(props.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('administration.credentials.title') }}</CardTitle>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Alert v-if="!canJudge" variant="destructive"><AlertTitle>{{ $t('administration.credentials.forbidden') }}</AlertTitle></Alert>
        <div v-else class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="credential-import-file">{{ $t('administration.credentials.importFile') }}</FieldLabel>
            <FileUpload id="credential-import-file" accept=".csv,.xlsx,.xls" :pending="importPending" :disabled="busy" @change="chooseFile" />
            <FieldDescription>{{ $t('administration.credentials.importHint') }}</FieldDescription>
          </Field>
          <Button :disabled="!selectedFile || busy" @click="importCredentials">
            <Spinner v-if="importPending" data-icon="inline-start" />{{ $t('administration.credentials.import') }}
          </Button>
          <Button variant="outline" :disabled="busy" @click="downloadTemplate">{{ $t('administration.credentials.downloadTemplate') }}</Button>
        </div>
        <Alert v-if="importError" variant="destructive"><AlertTitle>{{ $message(importError) }}</AlertTitle></Alert>
        <ScrollSurface v-if="importIssues.length" axis="both" class="max-h-80">
          <Table>
            <TableHeader><TableRow><TableHead>{{ $t('administration.credentials.rowNumber') }}</TableHead><TableHead>{{ $t('administration.credentials.issueDescription') }}</TableHead></TableRow></TableHeader>
            <TableBody><TableRow v-for="(issue, index) in importIssues" :key="index"><TableCell class="font-mono">{{ issue.rowNumber || '—' }}</TableCell><TableCell class="break-all">{{ $message(issue.description) }}</TableCell></TableRow></TableBody>
          </Table>
        </ScrollSurface>
      </CardContent>
    </Card>

    <Card v-if="canJudge">
      <CardHeader>
        <CardTitle>{{ $t('administration.credentials.batchTitle') }}</CardTitle>
        <CardDescription>{{ $t('administration.credentials.batchDescription') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <Field v-if="batches.length">
            <FieldLabel for="credential-batch-id">{{ $t('administration.credentials.batchId') }}</FieldLabel>
            <Select v-model="batchId" :disabled="busy">
              <SelectTrigger id="credential-batch-id" class="w-80"><SelectValue /></SelectTrigger>
              <SelectContent><SelectItem v-for="item in batches" :key="item.batchId" :value="item.batchId!">{{ item.sourceFileName }} · {{ item.batchId }}</SelectItem></SelectContent>
            </Select>
          </Field>
          <Field v-else>
            <FieldLabel for="credential-batch-id">{{ $t('administration.credentials.batchId') }}</FieldLabel>
            <Input id="credential-batch-id" v-model="batchId" class="w-80 font-mono" :disabled="busy" />
          </Field>
          <Button variant="outline" :disabled="loadPending || busy || !batchId" @click="loadBatch">
            <Spinner v-if="loadPending" data-icon="inline-start" />{{ $t('administration.credentials.load') }}
          </Button>
          <Button variant="outline" :disabled="busy || loadPending || !batch?.batchId" @click="downloadPackage">{{ $t('administration.credentials.downloadPackage') }}</Button>
          <Button variant="outline" :disabled="busy || loadPending || !batch?.batchId" @click="downloadPdf">{{ $t('administration.credentials.downloadPdf') }}</Button>
          <Button variant="destructive" :disabled="busy || loadPending || !batch?.batchId" @click="requestBatchRotation">
            <Spinner v-if="rotatePending" data-icon="inline-start" />{{ $t('administration.credentials.rotateBatch') }}
          </Button>
        </div>
        <ScrollSurface v-if="rows.length" axis="x" class="max-w-full">
          <Table class="min-w-[900px]">
            <TableHeader><TableRow><TableHead>{{ $t('administration.credentials.username') }}</TableHead><TableHead>{{ $t('administration.credentials.email') }}</TableHead><TableHead>{{ $t('administration.credentials.team') }}</TableHead><TableHead>{{ $t('administration.credentials.captain') }}</TableHead><TableHead>{{ $t('administration.credentials.password') }}</TableHead><TableHead>{{ $t('administration.credentials.fullName') }}</TableHead><TableHead>{{ $t('administration.credentials.studentNumber') }}</TableHead><TableHead>{{ $t('administration.credentials.school') }}</TableHead><TableHead>{{ $t('administration.credentials.track') }}</TableHead><TableHead class="text-right" /></TableRow></TableHeader>
            <TableBody>
              <TableRow v-for="row in rows" :key="`${row.userId}-${row.teamName}`">
                <TableCell class="font-mono">{{ row.userName }}</TableCell><TableCell>{{ row.email || '—' }}</TableCell><TableCell>{{ row.teamName }}</TableCell><TableCell>{{ row.captain ? $t('administration.credentials.captainYes') : $t('administration.credentials.captainNo') }}</TableCell><TableCell class="font-mono whitespace-nowrap">{{ row.password }}</TableCell><TableCell>{{ row.fullName || '—' }}</TableCell><TableCell class="font-mono">{{ row.studentNumber || '—' }}</TableCell><TableCell>{{ row.school || '—' }}</TableCell><TableCell>{{ row.trackKey }}</TableCell><TableCell class="text-right"><Button size="sm" variant="ghost" :disabled="busy || loadPending" @click="requestUserRotation(row.userId)">{{ $t('administration.credentials.rotateUser') }}</Button></TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </ScrollSurface>
        <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('administration.credentials.empty') }}</EmptyTitle></EmptyHeader></Empty>
        <OffsetPagination v-if="total" :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loadPending" @update:page="loadPage" @update:limit="setPageSize" />
      </CardContent>
    </Card>
    <AlertDialog v-model:open="confirmRotation">
      <AlertDialogContent>
        <AlertDialogHeader><AlertDialogTitle>{{ $t('administration.credentials.rotationTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('administration.credentials.rotationDescription') }}</AlertDialogDescription></AlertDialogHeader>
        <AlertDialogFooter><AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel><AlertDialogAction variant="destructive" :disabled="busy" @click="applyRotation">{{ $t('administration.credentials.confirmRotation') }}</AlertDialogAction></AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
