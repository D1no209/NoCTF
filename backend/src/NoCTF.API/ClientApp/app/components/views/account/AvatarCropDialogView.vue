<script setup lang="ts">
import { toRefs } from 'vue'
import type { AvatarCropDialogViewState } from '~/features/account/useAvatarCropDialog'

const viewProps = defineProps<{ state: AvatarCropDialogViewState }>()
const { RotateCcw, RotateCw, Scan, profileCover, cropWidth, cropHeight, emit, sourceImage, loadError, dragging, encoding, zoomPercent, resetCrop, rotate, handleWheel, startDrag, continueDrag, finishDrag, createCroppedFile, setPreviewFrameRef, setPreviewRef, open, saving } = toRefs(viewProps.state)
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent :class="profileCover ? 'sm:max-w-[900px]' : 'sm:max-w-[760px]'" :data-profile-cover-crop="profileCover || undefined">
      <DialogHeader>
        <DialogTitle>{{ profileCover ? $t('profile.cropCover') : $t('ui.cropAvatar') }}</DialogTitle>
        <DialogDescription>
          {{ profileCover ? $t('profile.coverCropDescription') : $t('ui.dragOnThePictureToAdjustThePositionAndScroll') }}
        </DialogDescription>
      </DialogHeader>

      <div class="grid gap-5" :class="profileCover ? '' : 'md:grid-cols-[minmax(0,360px)_minmax(0,1fr)]'">
        <div
          :ref="setPreviewFrameRef"
          class="relative mx-auto w-full touch-none select-none overflow-hidden rounded-lg border bg-muted outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/40"
          :class="[
            profileCover ? 'aspect-[4/1] max-w-[720px]' : 'aspect-square max-w-[360px]',
            dragging ? 'cursor-grabbing' : 'cursor-grab',
          ]"
          role="group"
          tabindex="0"
          :aria-label="profileCover ? $t('profile.coverCropPreview') : $t('ui.avatarCropPreview')"
          @dragstart.prevent
          @wheel.prevent="handleWheel"
          @pointerdown="startDrag"
          @pointermove="continueDrag"
          @pointerup="finishDrag"
          @pointercancel="finishDrag"
          @lostpointercapture="finishDrag"
        >
          <canvas
            :ref="setPreviewRef"
            :width="cropWidth"
            :height="cropHeight"
            class="size-full"
            :aria-label="profileCover ? $t('profile.coverCropCanvas') : $t('ui.avatarCropCanvas')"
          />
          <div
            v-if="!profileCover"
            class="pointer-events-none absolute inset-0 rounded-full border-2 border-dashed border-background/90 shadow-[0_0_0_999px_oklch(0_0_0/0.3)]"
          />
          <div
            v-if="loadError"
            class="absolute inset-0 grid place-items-center bg-background p-6 text-center text-sm text-destructive"
          > {{ $t('ui.thisImageCannotBeReadPleaseUseJpegPngOr') }} </div>
        </div>

        <div class="flex min-w-0 flex-col gap-5">
          <div class="rounded-lg border bg-muted/50 p-3 text-sm leading-6 text-muted-foreground"> {{ $t('ui.theScrollWheelZoomsCenteredOnThePointerPositionThe') }} </div>
          <div class="flex items-center justify-between border-b pb-3 text-sm">
            <span class="text-muted-foreground">{{ $t('ui.currentZoom') }}</span>
            <span class="font-mono font-semibold tabular-nums">{{ zoomPercent }}%</span>
          </div>
          <div class="flex flex-col gap-2">
            <span class="text-sm font-medium">{{ $t('ui.rotate') }}</span>
            <div class="grid grid-cols-2 gap-2">
              <Button variant="outline" type="button" @click="rotate(-90)">
                <RotateCcw />
                {{ $t('ui.90') }}
              </Button>
              <Button variant="outline" type="button" @click="rotate(90)">
                <RotateCw />
                {{ $t('ui.902') }}
              </Button>
            </div>
          </div>
          <Button variant="ghost" type="button" class="self-start" @click="resetCrop">
            <Scan /> {{ $t('ui.resetCropping') }} </Button>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="saving || encoding" @click="emit('update:open', false)"> {{ $t('ui.cancel') }} </Button>
        <Button :disabled="saving || encoding || !sourceImage || loadError" @click="createCroppedFile">
          <Spinner v-if="saving || encoding" data-icon="inline-start" /> {{ $t('ui.cropAndUpload') }} </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
