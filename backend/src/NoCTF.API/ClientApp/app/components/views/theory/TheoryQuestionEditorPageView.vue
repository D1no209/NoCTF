<script setup lang="ts">
import { toRefs } from 'vue'
import type { TheoryQuestionEditorState } from '~/features/theory/useTheoryQuestionEditorPage'
const props = defineProps<{ state: TheoryQuestionEditorState }>()
const { state } = toRefs(props)
</script>
<template>
  <section class="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-8">
    <div class="flex flex-wrap items-center justify-between gap-3"><Button variant="ghost" @click="state.back">{{ $t('theory.back') }}</Button><h1 class="text-2xl font-semibold">{{ state.detail?.question?.title ?? $t('theory.newQuestion') }}</h1><Badge v-if="state.detail">{{ $t('theory.version', { version: state.detail.question?.version ?? 1 }) }}</Badge></div>
    <Alert v-if="state.error" variant="destructive"><AlertDescription>{{ $message(state.error) }}</AlertDescription></Alert>
    <Skeleton v-if="state.loading" class="h-96" />
    <UiForm v-else-if="!state.id || state.detail" validation="feature" @submit="state.save">
      <Card><CardContent class="flex flex-col gap-6 pt-6">
        <FieldGroup class="grid gap-5 md:grid-cols-2">
          <Field><FieldLabel for="theory-title">{{ $t('theory.title') }}</FieldLabel><Input id="theory-title" v-model="state.form.title" :disabled="state.busy" /></Field>
          <Field><FieldLabel>{{ $t('theory.kind') }}</FieldLabel><Select :model-value="String(state.form.kind ?? 0)" :disabled="state.busy" @update:model-value="state.changeKind"><SelectTrigger :aria-label="$t('theory.kind')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="kind in state.kinds" :key="kind" :value="String(kind)">{{ state.questionKindLabel(kind) }}</SelectItem></SelectGroup></SelectContent></Select></Field>
          <Field><FieldLabel for="theory-knowledge">{{ $t('theory.knowledge') }}</FieldLabel><Input id="theory-knowledge" v-model="state.knowledge" :disabled="state.busy" /></Field>
          <Field><FieldLabel for="theory-tags">{{ $t('theory.tags') }}</FieldLabel><Input id="theory-tags" v-model="state.tags" :disabled="state.busy" /></Field>
          <Field><FieldLabel>{{ $t('theory.difficulty') }}</FieldLabel><Select v-model="state.form.difficulty" :disabled="state.busy"><SelectTrigger :aria-label="$t('theory.difficulty')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem :value="0">{{ $t('theory.basic') }}</SelectItem><SelectItem :value="1">{{ $t('theory.intermediate') }}</SelectItem><SelectItem :value="2">{{ $t('theory.advanced') }}</SelectItem></SelectGroup></SelectContent></Select></Field>
          <Field v-if="state.id"><FieldLabel>{{ $t('theory.state') }}</FieldLabel><Select :model-value="String(state.detail?.question?.state ?? 0)" :disabled="state.busy" @update:model-value="state.setState"><SelectTrigger :aria-label="$t('theory.state')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem value="0">{{ $t('theory.draft') }}</SelectItem><SelectItem value="1">{{ $t('theory.available') }}</SelectItem><SelectItem value="2">{{ $t('theory.retired') }}</SelectItem></SelectGroup></SelectContent></Select></Field>
        </FieldGroup>
        <Separator />
        <Field><FieldLabel>{{ $t('theory.body') }}</FieldLabel><MarkdownEditor v-model="state.form.body" :label="$t('theory.body')" :disabled="state.busy" @save="state.save" /></Field>
        <FieldSet><FieldLegend>{{ $t('theory.options') }}</FieldLegend><FieldGroup>
          <div v-for="(option, index) in state.form.options" :key="index" class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-3 sm:grid-cols-[auto_minmax(0,1fr)_auto_auto]">
            <span class="pt-2 font-semibold">{{ state.optionLabel(index) }}</span>
            <Textarea v-if="state.form.kind !== 2" v-model="option.content" :aria-label="state.optionLabel(index)" :disabled="state.busy" class="min-h-20 min-w-0 flex-1" />
            <span v-else class="flex-1 pt-2">{{ option.truthValue === 0 ? $t('theory.true') : $t('theory.false') }}</span>
            <Field orientation="horizontal" class="col-start-2 w-auto shrink-0 pt-2 sm:col-start-auto"><Checkbox :id="`correct-${index}`" :model-value="option.isCorrect" :disabled="state.busy" @update:model-value="state.setCorrect(index, $event)" /><FieldLabel :for="`correct-${index}`" class="whitespace-nowrap">{{ $t('theory.correct') }}</FieldLabel></Field>
            <Button v-if="state.form.kind !== 2" type="button" variant="ghost" :disabled="state.busy || state.form.options.length <= 2" @click="state.removeOption(index)">{{ $t('theory.remove') }}</Button>
          </div>
        </FieldGroup><Button v-if="state.form.kind !== 2" type="button" variant="outline" :disabled="state.busy || state.form.options.length >= 8" @click="state.addOption">{{ $t('theory.addOption') }}</Button></FieldSet>
        <Field><FieldLabel>{{ $t('theory.explanation') }}</FieldLabel><Textarea v-model="state.explanation" :disabled="state.busy" :aria-label="$t('theory.explanation')" /></Field>
        <Field><FieldLabel>{{ $t('theory.images') }}</FieldLabel><FileUpload accept="image/png,image/jpeg,image/webp" :disabled="!state.id" :pending="state.busy" @change="state.selectFile" /><FieldDescription>{{ $t('theory.imageHelp') }}</FieldDescription></Field>
        <Separator />
        <div class="flex items-center justify-between gap-3"><h2 class="font-semibold">{{ $t('theory.preview') }}</h2><Button type="button" variant="ghost" @click="state.shufflePreview">{{ $t('theory.shufflePreview') }}</Button></div>
        <MarkdownContent :source="state.form.body" :assets="state.assets" />
        <div v-for="(index, position) in state.previewOrder" :key="index" class="flex items-start gap-3"><span>{{ state.optionLabel(position) }}</span><span v-if="state.form.kind === 2">{{ state.form.options[index]?.truthValue === 0 ? $t('theory.true') : $t('theory.false') }}</span><MarkdownContent v-else :source="state.form.options[index]?.content ?? ''" :assets="state.assets" /><Badge v-if="state.form.options[index]?.isCorrect" variant="secondary">{{ $t('theory.correct') }}</Badge></div>
        <template v-if="state.explanation"><h2 class="font-semibold">{{ $t('theory.explanation') }}</h2><MarkdownContent :source="state.explanation" :assets="state.assets" /></template>
      </CardContent><CardFooter class="flex flex-wrap gap-3"><Button type="submit" :disabled="state.busy"><Spinner v-if="state.busy" data-icon="inline-start" />{{ $t('theory.save') }}</Button><span v-if="state.dirty" class="text-sm text-muted-foreground">{{ $t('theory.unsaved') }}</span><Button v-if="state.id && state.canOrganize" type="button" variant="outline" :disabled="state.busy || state.dirty" @click="state.copy">{{ $t('theory.copy') }}</Button></CardFooter></Card>
    </UiForm>
    <Card v-if="state.id && state.detail?.question?.canManage"><CardHeader><CardTitle>{{ $t('theory.permissions') }}</CardTitle></CardHeader><CardContent class="flex flex-col gap-4"><Table v-if="state.detail.grants?.length"><TableBody><TableRow v-for="grant in state.detail.grants" :key="grant.userId"><TableCell>{{ grant.name }}</TableCell><TableCell>{{ grant.permission === 0 ? $t('theory.use') : grant.permission === 1 ? $t('theory.edit') : $t('theory.manage') }}</TableCell><TableCell><Button variant="ghost" :disabled="state.busy" @click="state.revokeGrant(grant.userId)">{{ $t('theory.revoke') }}</Button></TableCell></TableRow></TableBody></Table><FieldGroup class="grid gap-4 md:grid-cols-2"><Field><FieldLabel for="theory-grant-user">{{ $t('theory.userId') }}</FieldLabel><Input id="theory-grant-user" v-model="state.permissionUser" :disabled="state.busy" /></Field><Field><FieldLabel>{{ $t('theory.permission') }}</FieldLabel><Select v-model="state.permission"><SelectTrigger :aria-label="$t('theory.permission')"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem value="0">{{ $t('theory.use') }}</SelectItem><SelectItem value="1">{{ $t('theory.edit') }}</SelectItem><SelectItem value="2">{{ $t('theory.manage') }}</SelectItem></SelectGroup></SelectContent></Select></Field></FieldGroup><Button class="self-start" :disabled="state.busy || !state.permissionUser" @click="state.grant()">{{ $t('theory.grant') }}</Button></CardContent></Card>
    <AlertDialog :open="state.leaveOpen" @update:open="state.setLeaveOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('theory.unsaved') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('theory.discardQuestion') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel @click="state.stay">{{ $t('theory.stay') }}</AlertDialogCancel><AlertDialogAction @click="state.discard">{{ $t('theory.discard') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>
</template>
