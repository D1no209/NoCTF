<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdMyWriteUpPageViewState } from '~/features/routes/competitions/[id]/my/useCompetitionsByIdMyWriteUpPage'

const viewProps = defineProps<{ state: CompetitionsByIdMyWriteUpPageViewState }>()
const { Download, Eye, FileText, maximumWriteUpBytes, competitionId, submissionRequired, submissionDeadlineAt, submissionClosed, writeUp, selectedFile, uploadInputKey, loading, loadError, uploadError, uploadPending, previewUrl, previewLoading, previewError, downloadPending, load, selectFile, submit, preview, download, CompetitionParticipantWorkspace } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId">
    <Card class="min-h-[38rem]">
      <CardHeader>
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-3">
            <FileText class="size-6 text-primary" />
            <CardTitle>{{ $t('writeUp.myWriteUp') }}</CardTitle>
          </div>
          <Badge v-if="writeUp" variant="default">{{ $t('writeUp.submitted') }}</Badge>
          <Badge v-else-if="submissionClosed" variant="destructive">{{ $t('writeUp.submissionClosed') }}</Badge>
          <Badge v-else variant="secondary">{{ $t('writeUp.notSubmitted') }}</Badge>
        </div>
        <CardDescription>{{ $t('writeUp.participantDescription', { size: formatBytes(maximumWriteUpBytes) }) }}</CardDescription>
      </CardHeader>
      <CardContent class="flex min-h-0 flex-col gap-5">
        <Alert v-if="loadError" variant="destructive">
          <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
            <span>{{ $message(loadError) }}</span>
            <Button variant="outline" size="sm" @click="load">{{ $t('common.label.retry') }}</Button>
          </AlertDescription>
        </Alert>
        <Skeleton v-else-if="loading" class="h-56 w-full" />
        <template v-else>
          <Alert v-if="!submissionRequired">
            <AlertDescription>{{ $t('writeUp.notRequiredForCompetition') }}</AlertDescription>
          </Alert>
          <Alert v-if="submissionClosed" variant="destructive">
            <AlertDescription>
              {{ $t('writeUp.deadlinePassed', { deadline: formatDateTime(submissionDeadlineAt) }) }}
            </AlertDescription>
          </Alert>
          <Alert v-else>
            <AlertDescription>
              {{ $t('writeUp.deadlineOpen', { deadline: formatDateTime(submissionDeadlineAt) }) }}
            </AlertDescription>
          </Alert>
          <UiForm v-if="!submissionClosed" validation="feature" class="flex flex-col gap-3" @submit.prevent="submit">
            <Field>
              <FieldLabel for="team-writeup-file">{{ writeUp ? $t('writeUp.replacePdf') : $t('writeUp.selectPdf') }}</FieldLabel>
              <FileUpload
                :key="uploadInputKey ?? undefined"
                id="team-writeup-file"
                accept="application/pdf,.pdf"
                :pending="uploadPending"
                :error="uploadError"
                @change="selectFile"
              />
              <FieldDescription>{{ $t('writeUp.pdfRequirement', { size: formatBytes(maximumWriteUpBytes) }) }}</FieldDescription>
            </Field>
            <div>
              <Button type="submit" :disabled="!selectedFile || uploadPending">
                <Spinner v-if="uploadPending" data-icon="inline-start" />
                {{ writeUp ? $t('writeUp.replace') : $t('writeUp.submit') }}
              </Button>
            </div>
          </UiForm>

          <template v-if="writeUp">
            <Separator />
            <div class="flex flex-wrap items-start justify-between gap-4">
              <dl class="grid min-w-0 gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
                <dt class="text-muted-foreground">{{ $t('writeUp.fileName') }}</dt>
                <dd class="break-all font-medium">{{ writeUp.fileName }}</dd>
                <dt class="text-muted-foreground">{{ $t('common.label.fileSize') }}</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(writeUp.byteLength ?? 0) }}</dd>
                <dt class="text-muted-foreground">{{ $t('writeUp.submittedAt') }}</dt>
                <dd class="font-mono tabular-nums">{{ formatDateTime(writeUp.submittedAt) }}</dd>
                <dt class="text-muted-foreground">{{ $t('writeups.label.sha') }}</dt>
                <dd class="break-all font-mono text-xs">{{ writeUp.sha256 }}</dd>
              </dl>
              <div class="flex flex-wrap gap-2">
                <Button variant="outline" :disabled="previewLoading" @click="preview">
                  <Spinner v-if="previewLoading" data-icon="inline-start" />
                  <Eye v-else data-icon="inline-start" />{{ $t('writeUp.preview') }}
                </Button>
                <Button variant="outline" :disabled="downloadPending" @click="download">
                  <Spinner v-if="downloadPending" data-icon="inline-start" />
                  <Download v-else data-icon="inline-start" />{{ $t('writeups.label.download') }}
                </Button>
              </div>
            </div>
            <PdfPreview
              v-if="previewUrl || previewLoading || previewError"
              :source="previewUrl"
              :loading="previewLoading"
              :error="previewError"
              :accessible-label="$t('writeUp.previewTitle', { team: writeUp.teamName ?? '' })"
              :empty-label="$t('writeUp.previewEmpty')"
            />
          </template>
        </template>
      </CardContent>
    </Card>
  </component>
</template>
