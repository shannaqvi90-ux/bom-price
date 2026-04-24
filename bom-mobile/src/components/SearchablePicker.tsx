import { useMemo, useState } from "react";
import { FlatList, Modal, Pressable, Text, TextInput, View } from "react-native";

interface Option {
  id: number;
  label: string;
  sublabel?: string;
}

interface Props {
  label?: string;
  placeholder?: string;
  value: number | null;
  options: Option[];
  onChange: (id: number) => void;
  loading?: boolean;
  error?: string;
}

export function SearchablePicker({
  label,
  placeholder = "Select...",
  value,
  options,
  onChange,
  loading,
  error,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");

  const selected = useMemo(
    () => options.find((o) => o.id === value) ?? null,
    [options, value]
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter(
      (o) =>
        o.label.toLowerCase().includes(q) ||
        (o.sublabel ?? "").toLowerCase().includes(q)
    );
  }, [options, query]);

  return (
    <View className="mb-3">
      {label ? <Text className="text-sm text-slate-700 mb-1">{label}</Text> : null}
      <Pressable
        onPress={() => setOpen(true)}
        className={`border rounded-md px-3 py-3 bg-white ${error ? "border-rose-500" : "border-slate-300"}`}
      >
        <Text className={selected ? "text-slate-900" : "text-slate-400"}>
          {loading ? "Loading..." : selected ? selected.label : placeholder}
        </Text>
      </Pressable>
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}

      <Modal visible={open} animationType="slide" onRequestClose={() => setOpen(false)}>
        <View className="flex-1 bg-slate-50">
          <View className="px-4 pt-12 pb-2 flex-row items-center">
            <Pressable onPress={() => setOpen(false)} className="py-2 pr-4">
              <Text className="text-brand-600 text-base">Cancel</Text>
            </Pressable>
            <Text className="text-lg font-semibold text-slate-900 flex-1">{label}</Text>
          </View>
          <View className="px-4 pb-2">
            <TextInput
              value={query}
              onChangeText={setQuery}
              placeholder="Search..."
              autoFocus
              placeholderTextColor="#94a3b8"
              className="border border-slate-300 rounded-md px-3 py-2 bg-white text-slate-900"
            />
          </View>
          <FlatList
            data={filtered}
            keyExtractor={(o) => String(o.id)}
            renderItem={({ item }) => (
              <Pressable
                onPress={() => {
                  onChange(item.id);
                  setQuery("");
                  setOpen(false);
                }}
                className="px-4 py-3 border-b border-slate-200 bg-white"
              >
                <Text className="text-base text-slate-900">{item.label}</Text>
                {item.sublabel ? (
                  <Text className="text-xs text-slate-500 mt-0.5">{item.sublabel}</Text>
                ) : null}
              </Pressable>
            )}
            ListEmptyComponent={
              <Text className="text-center text-slate-500 p-6">No matches</Text>
            }
          />
        </View>
      </Modal>
    </View>
  );
}

export type { Option as SearchablePickerOption };
