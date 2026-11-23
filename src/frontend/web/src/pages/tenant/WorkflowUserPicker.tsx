import { useEffect, useState } from 'react'
import { Autocomplete, TextField } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { workflowApi, type WorkflowUser } from '../../api/workflow'

/** Searchable picker over users eligible to act on approvals (server-side search, first 20 matches).
 * `delegates` excludes the caller; `assignees` (workflow administrators resuming a stalled request) includes them. */
export function EligibleUserPicker({ value, onChange, label, source = 'delegates' }: {
  value: WorkflowUser | null; onChange: (u: WorkflowUser | null) => void; label: string; source?: 'delegates' | 'assignees'
}) {
  const [input, setInput] = useState('')
  const [term, setTerm] = useState('')
  useEffect(() => {
    const t = setTimeout(() => setTerm(input.trim()), 250)
    return () => clearTimeout(t)
  }, [input])
  const q = useQuery({
    queryKey: ['wf-eligible', source, term],
    queryFn: () => source === 'assignees' ? workflowApi.eligibleAssignees(term, 1, 20) : workflowApi.eligibleDelegates(term, 1, 20),
  })
  const more = (q.data?.totalCount ?? 0) - (q.data?.items.length ?? 0)
  return (
    <Autocomplete
      size="small"
      options={q.data?.items ?? []}
      value={value}
      loading={q.isLoading}
      filterOptions={x => x}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      getOptionLabel={u => `${u.displayName} (${u.email})`}
      onChange={(_, u) => onChange(u)}
      onInputChange={(_, v, reason) => { if (reason !== 'reset') setInput(v) }}
      noOptionsText={term ? 'No eligible user matches' : 'No eligible users'}
      renderInput={params => (
        <TextField {...params} label={label} placeholder="Search by name or email"
          helperText={more > 0 ? `${more} more match${more === 1 ? '' : 'es'} — type to narrow the search` : ' '} />
      )}
    />
  )
}
