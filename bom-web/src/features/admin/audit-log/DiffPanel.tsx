interface Props {
  before: string;
  after?: string | null;
}

export function DiffPanel({ before, after }: Props) {
  return (
    <div className="grid grid-cols-2 gap-2 text-xs">
      <div>
        <div className="font-semibold mb-1">Before</div>
        <pre className="bg-red-50 border border-red-200 p-2 rounded overflow-auto max-h-64">{before}</pre>
      </div>
      <div>
        <div className="font-semibold mb-1">After</div>
        <pre className="bg-green-50 border border-green-200 p-2 rounded overflow-auto max-h-64">{after ?? "(deleted)"}</pre>
      </div>
    </div>
  );
}
