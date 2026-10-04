<script setup lang="ts">
import { toRefs } from 'vue'
import type { UiCheckViewState } from '~/features/development/useUiCheck'
const props = defineProps<{ state: UiCheckViewState }>()
const { isDark, toggleTheme, isEnglish, switchLocale, name, amount, when, selection, submitted, fileCount, submit, showError, fileChanged, dialogOpen, dialogName, submitDialog } = toRefs(props.state)
</script>

<template>
  <main id="preview" class="ui-preview-stage mx-auto flex max-w-4xl flex-col gap-8 px-6 py-8">
    <header class="ui-preview-toolbar flex flex-wrap items-center justify-between gap-4">
      <div><h1 class="text-2xl font-semibold">{{ $t('preview.title') }}</h1><p class="mt-2 text-sm text-muted-foreground">{{ $t('preview.description') }}</p></div>
      <div class="flex gap-2">
        <Button id="probe-theme" variant="outline" @click="toggleTheme">{{ isDark ? $t('common.label.switchLightTheme') : $t('common.label.switchDarkTheme') }}</Button>
        <Button id="probe-locale" variant="outline" @click="switchLocale">{{ isEnglish ? $t('common.label.switchChinese') : $t('common.label.switchEnglish') }}</Button>
      </div>
    </header>
    <Card id="probe-card">
      <CardHeader><CardTitle>{{ $t('preview.controls') }}</CardTitle><CardDescription>{{ $t('preview.hint') }}</CardDescription></CardHeader>
      <CardContent>
        <UiForm @submit="submit"><FieldGroup class="grid grid-cols-6 gap-x-6 gap-y-5">
          <Field class="col-span-6 min-w-0 @xl/field-group:col-span-4"><FieldLabel for="probe-name">{{ $t('preview.name') }}</FieldLabel><Input id="probe-name" v-model="name" required minlength="3" /></Field>
          <Field class="col-span-6 min-w-0 @xl/field-group:col-span-2"><FieldLabel for="probe-number">{{ $t('preview.amount') }}</FieldLabel><NumberInput id="probe-number" v-model="amount" :min="0" :max="20" required /></Field>
          <Field class="col-span-6 min-w-0 @xl/field-group:col-span-3"><FieldLabel for="probe-date">{{ $t('dateTime.open') }}</FieldLabel><DateTimePicker id="probe-date" v-model="when" required /></Field>
          <Field class="col-span-6 min-w-0 @xl/field-group:col-span-3"><FieldLabel for="probe-select">{{ $t('preview.choice') }}</FieldLabel>
            <Select v-model="selection"><SelectTrigger id="probe-select"><SelectValue :placeholder="$t('preview.choice')" /></SelectTrigger>
              <SelectContent position="popper"><SelectGroup><SelectItem v-for="i in 50" :key="i" :value="String(i)">{{ $t('preview.option', { index: i }) }}</SelectItem></SelectGroup></SelectContent>
            </Select>
          </Field>
          <Field class="col-span-6 min-w-0"><FieldLabel for="probe-upload">{{ $t('upload.choose') }}</FieldLabel><FileUpload id="probe-upload" multiple @change="fileChanged" /><FieldDescription v-if="fileCount">{{ $t('preview.files', { count: fileCount }) }}</FieldDescription></Field>
          <Field orientation="horizontal" class="col-span-6 flex-wrap pt-1"><Button id="probe-submit" type="submit">{{ $t('preview.validate') }}</Button><Button variant="outline" @click="showError">{{ $t('preview.showError') }}</Button></Field>
        </FieldGroup></UiForm>
        <p v-if="submitted" class="mt-4 text-sm text-primary">{{ $t('preview.valid') }}</p>
      </CardContent>
    </Card>
    <section class="flex flex-col gap-3"><h2 class="text-lg font-semibold">{{ $t('preview.scroll') }}</h2>
      <ScrollSurface id="probe-scroll" axis="y" class="h-48 border p-4" :aria-label="$t('preview.scroll')"><p v-for="i in 60" :key="i">{{ $t('preview.option', { index: i }) }}</p></ScrollSurface>
    </section>
    <div class="flex flex-wrap items-center gap-4"><Hint :content="$t('preview.tooltip')"><Button id="probe-hint" variant="outline">{{ $t('preview.tooltip') }}</Button></Hint><MarkdownContent :source="$t('preview.markdown')" /></div>
    <Dialog v-model:open="dialogOpen"><DialogTrigger as-child><Button id="probe-dialog" variant="outline">{{ $t('preview.dialog') }}</Button></DialogTrigger><DialogContent>
      <DialogHeader><DialogTitle>{{ $t('preview.dialog') }}</DialogTitle><DialogDescription>{{ $t('preview.description') }}</DialogDescription></DialogHeader>
      <UiForm @submit="submitDialog"><FieldGroup><Field><FieldLabel for="probe-dialog-name">{{ $t('preview.name') }}</FieldLabel><Input id="probe-dialog-name" v-model="dialogName" required /></Field><Button id="probe-dialog-submit" type="submit">{{ $t('preview.validate') }}</Button></FieldGroup></UiForm>
    </DialogContent></Dialog>
  </main>
</template>
