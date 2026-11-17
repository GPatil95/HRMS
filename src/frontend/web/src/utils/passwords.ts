/** Generates a 16-char password satisfying the platform policy (letter + digit, min 12). */
export function generatePassword(): string {
  const letters = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ'
  const all = letters + '23456789'
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  const chars = Array.from(bytes, (b) => all[b % all.length])
  chars[0] = letters[bytes[0]! % letters.length]!
  chars[15] = String(2 + (bytes[15]! % 8))
  return chars.join('')
}

/** Rough 0-4 strength score for the UI meter only — the server enforces the real policy. */
export function passwordStrength(pw: string): { score: 0 | 1 | 2 | 3 | 4; label: string } {
  if (!pw) return { score: 0, label: 'Empty' }
  let s = 0
  if (pw.length >= 12) s++
  if (pw.length >= 16) s++
  if (/[a-z]/.test(pw) && /[A-Z]/.test(pw)) s++
  if (/\d/.test(pw) && /[^a-zA-Z0-9]/.test(pw)) s++
  const labels = ['Very weak', 'Weak', 'Fair', 'Good', 'Strong'] as const
  return { score: s as 0 | 1 | 2 | 3 | 4, label: labels[s]! }
}

/** Mirrors the backend password policy messages for inline validation. */
export function passwordPolicyError(pw: string): string | null {
  if (pw.length < 12) return 'At least 12 characters required'
  if (!/[a-zA-Z]/.test(pw)) return 'Must contain at least one letter'
  if (!/\d/.test(pw)) return 'Must contain at least one digit'
  return null
}
