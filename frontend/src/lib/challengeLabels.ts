const LABELS: Record<
  string,
  {
    text: string
    className: string
    neonClassName: string
    modalClassName: string
    cabinetClassName: string
    pinClassName: string
    lineClassName: string
  }
> = {
  WEB: {
    text: 'WEB-01',
    className: 'px-3 bg-neon-web text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-web shadow-[0_0_8px_var(--neon-web)]',
    modalClassName: 'border-t-neon-web',
    cabinetClassName: 'border-t-neon-web shadow-[0_0_12px_var(--neon-web)]',
    pinClassName: 'bg-neon-web',
    lineClassName: 'stroke-neon-web',
  },
  PWN: {
    text: 'PWN-02',
    className: 'px-3 bg-neon-pwn text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-pwn shadow-[0_0_8px_var(--neon-pwn)]',
    modalClassName: 'border-t-neon-pwn',
    cabinetClassName: 'border-t-neon-pwn shadow-[0_0_12px_var(--neon-pwn)]',
    pinClassName: 'bg-neon-pwn',
    lineClassName: 'stroke-neon-pwn',
  },
  MISC: {
    text: 'MISC-03',
    className: 'px-3 bg-neon-misc text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-misc shadow-[0_0_8px_var(--neon-misc)]',
    modalClassName: 'border-t-neon-misc',
    cabinetClassName: 'border-t-neon-misc shadow-[0_0_12px_var(--neon-misc)]',
    pinClassName: 'bg-neon-misc',
    lineClassName: 'stroke-neon-misc',
  },
  REVERSE: {
    text: 'REV-04',
    className: 'px-3 bg-neon-reverse text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-reverse shadow-[0_0_8px_var(--neon-reverse)]',
    modalClassName: 'border-t-neon-reverse',
    cabinetClassName: 'border-t-neon-reverse shadow-[0_0_12px_var(--neon-reverse)]',
    pinClassName: 'bg-neon-reverse',
    lineClassName: 'stroke-neon-reverse',
  },
  MOBILE: {
    text: 'MOB-05',
    className: 'px-3 bg-neon-mobile text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-mobile shadow-[0_0_8px_var(--neon-mobile)]',
    modalClassName: 'border-t-neon-mobile',
    cabinetClassName: 'border-t-neon-mobile shadow-[0_0_12px_var(--neon-mobile)]',
    pinClassName: 'bg-neon-mobile',
    lineClassName: 'stroke-neon-mobile',
  },
  CRYPTO: {
    text: 'CRYPTO-06',
    className: 'px-3 bg-neon-crypto text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-crypto shadow-[0_0_8px_var(--neon-crypto)]',
    modalClassName: 'border-t-neon-crypto',
    cabinetClassName: 'border-t-neon-crypto shadow-[0_0_12px_var(--neon-crypto)]',
    pinClassName: 'bg-neon-crypto',
    lineClassName: 'stroke-neon-crypto',
  },
  FORENSICS: {
    text: 'FORENS-07',
    className: 'px-3 bg-neon-forensics text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-forensics shadow-[0_0_8px_var(--neon-forensics)]',
    modalClassName: 'border-t-neon-forensics',
    cabinetClassName: 'border-t-neon-forensics shadow-[0_0_12px_var(--neon-forensics)]',
    pinClassName: 'bg-neon-forensics',
    lineClassName: 'stroke-neon-forensics',
  },
  AI: {
    text: 'AI-08',
    className: 'px-3 bg-neon-ai text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-ai shadow-[0_0_8px_var(--neon-ai)]',
    modalClassName: 'border-t-neon-ai',
    cabinetClassName: 'border-t-neon-ai shadow-[0_0_12px_var(--neon-ai)]',
    pinClassName: 'bg-neon-ai',
    lineClassName: 'stroke-neon-ai',
  },
  BLOCKCHAIN: {
    text: 'CHAIN-09',
    className: 'px-3 bg-neon-blockchain text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-blockchain shadow-[0_0_8px_var(--neon-blockchain)]',
    modalClassName: 'border-t-neon-blockchain',
    cabinetClassName: 'border-t-neon-blockchain shadow-[0_0_12px_var(--neon-blockchain)]',
    pinClassName: 'bg-neon-blockchain',
    lineClassName: 'stroke-neon-blockchain',
  },
  HARDWARE: {
    text: 'HW-10',
    className: 'px-3 bg-neon-hardware text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-hardware shadow-[0_0_8px_var(--neon-hardware)]',
    modalClassName: 'border-t-neon-hardware',
    cabinetClassName: 'border-t-neon-hardware shadow-[0_0_12px_var(--neon-hardware)]',
    pinClassName: 'bg-neon-hardware',
    lineClassName: 'stroke-neon-hardware',
  },
  OSINT: {
    text: 'OSINT-11',
    className: 'px-3 bg-neon-osint text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-osint shadow-[0_0_8px_var(--neon-osint)]',
    modalClassName: 'border-t-neon-osint',
    cabinetClassName: 'border-t-neon-osint shadow-[0_0_12px_var(--neon-osint)]',
    pinClassName: 'bg-neon-osint',
    lineClassName: 'stroke-neon-osint',
  },
  CLOUD: {
    text: 'CLOUD-12',
    className: 'px-3 bg-neon-cloud text-[var(--neon-foreground)] border-[var(--neon-foreground)]/25',
    neonClassName: 'border-l-neon-cloud shadow-[0_0_8px_var(--neon-cloud)]',
    modalClassName: 'border-t-neon-cloud',
    cabinetClassName: 'border-t-neon-cloud shadow-[0_0_12px_var(--neon-cloud)]',
    pinClassName: 'bg-neon-cloud',
    lineClassName: 'stroke-neon-cloud',
  },
}

export function challengeTypeLabel(value?: string | null) {
  const key = (value ?? 'MISC').trim().toUpperCase()
  return LABELS[key] ?? LABELS.MISC
}
