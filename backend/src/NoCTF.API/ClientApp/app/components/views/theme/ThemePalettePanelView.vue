<script setup lang="ts">
import { toRefs } from 'vue'
import type { ThemePalettePanelViewState } from '~/features/theme/useThemePalettePanel'
const props = defineProps<{ state: ThemePalettePanelViewState }>()
const { Palette, mode, color, wallpaperBlur, maximumWallpaperBlur, presets, setColor, setWallpaperBlur, setMode, reset } = toRefs(props.state)
</script>
<template>
  <Popover>
    <PopoverTrigger as-child><Button variant="ghost" size="icon" class="rounded-full" :aria-label="$t('palette.title')"><Palette /></Button></PopoverTrigger>
    <PopoverContent align="end" class="w-80 max-h-[calc(100dvh-6rem)]">
      <div class="flex flex-col gap-4">
        <PopoverHeader><PopoverTitle>{{ $t('palette.title') }}</PopoverTitle><PopoverDescription>{{ $t('palette.description') }}</PopoverDescription></PopoverHeader>
        <div class="grid grid-cols-2 gap-2">
          <Button size="sm" :variant="mode === 'light' ? 'default' : 'ghost'" :aria-pressed="mode === 'light'" @click="setMode('light')">{{ $t('palette.light') }}</Button>
          <Button size="sm" :variant="mode === 'dark' ? 'default' : 'ghost'" :aria-pressed="mode === 'dark'" @click="setMode('dark')">{{ $t('palette.dark') }}</Button>
        </div>
        <ColorPicker id="theme-color" :model-value="color" @update:model-value="setColor" />
        <BlurSlider id="theme-wallpaper-blur" :label="$t('palette.wallpaperBlur')" :model-value="wallpaperBlur" :maximum="maximumWallpaperBlur" @update:model-value="setWallpaperBlur" />
        <div class="grid grid-cols-8 gap-1" :aria-label="$t('palette.presets')">
          <ColorSwatch v-for="preset in presets" :key="preset.color" :color="preset.color" :label="$t(preset.label)" :selected="color === preset.color" class="size-8" @select="setColor" />
        </div>
        <Button variant="outline" size="sm" @click="reset">{{ $t('palette.reset') }}</Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
