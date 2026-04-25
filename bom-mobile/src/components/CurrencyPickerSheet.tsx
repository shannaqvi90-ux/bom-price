import { useState } from "react";
import { Modal, Pressable, Text, View } from "react-native";

interface Props {
  value: string;
  options: string[];
  onChange: (code: string) => void;
}

/**
 * Currency picker for the Accountant V2.1 costing form. A bottom-sheet style modal
 * with a small list of options — currencies are short and the list is typically
 * under 10 entries, so search/filter is not needed (unlike SearchablePicker which
 * is keyed by numeric id and built for long catalogs).
 */
export function CurrencyPickerSheet({ value, options, onChange }: Props) {
  const [open, setOpen] = useState(false);

  return (
    <View>
      <Pressable
        onPress={() => setOpen(true)}
        style={{
          borderWidth: 1,
          borderColor: "#cbd5e1",
          borderRadius: 8,
          paddingVertical: 10,
          paddingHorizontal: 12,
          backgroundColor: "#fff",
          alignItems: "center",
        }}
      >
        <Text style={{ fontSize: 14, fontWeight: "600", color: "#1e40af" }}>{value} ▼</Text>
      </Pressable>

      <Modal visible={open} animationType="slide" transparent onRequestClose={() => setOpen(false)}>
        <Pressable
          onPress={() => setOpen(false)}
          style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}
        >
          <Pressable
            onPress={(e) => e.stopPropagation()}
            style={{
              backgroundColor: "#fff",
              borderTopLeftRadius: 16,
              borderTopRightRadius: 16,
              paddingHorizontal: 16,
              paddingTop: 16,
              paddingBottom: 32,
              maxHeight: "70%",
            }}
          >
            <Text style={{ fontSize: 16, fontWeight: "700", marginBottom: 12 }}>
              Select currency
            </Text>
            {options.map((code) => {
              const selected = code === value;
              return (
                <Pressable
                  key={code}
                  onPress={() => {
                    onChange(code);
                    setOpen(false);
                  }}
                  style={({ pressed }) => ({ opacity: pressed ? 0.7 : 1 })}
                >
                  <View
                    style={{
                      paddingVertical: 12,
                      paddingHorizontal: 12,
                      borderRadius: 8,
                      backgroundColor: selected ? "#eff6ff" : "transparent",
                      marginBottom: 4,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 15,
                        fontWeight: selected ? "700" : "400",
                        color: selected ? "#1e40af" : "#0f172a",
                      }}
                    >
                      {code}
                      {selected ? "  ✓" : ""}
                    </Text>
                  </View>
                </Pressable>
              );
            })}
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}
