interface Props {
  ownerName: string;
  prefix?: string;
}

export function OwnedByBadge({ ownerName, prefix = "by" }: Props) {
  return (
    <span className="text-xs text-slate-500 ml-2">
      {prefix} {ownerName}
    </span>
  );
}
