export function updateNullableNumber(
  values: readonly (number | null)[],
  index: number,
  value: number | null,
): Array<number | null> {
  return values.map((current, currentIndex) => currentIndex === index ? value : current)
}
