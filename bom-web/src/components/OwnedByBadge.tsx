interface Props {
  ownerName: string;
  prefix?: string;
}

export function OwnedByBadge({ ownerName, prefix = "by" }: Props) {
  return (
    <span className="text-xs text-muted-foreground ml-2">
      {prefix} {ownerName}
    </span>
  );
}
