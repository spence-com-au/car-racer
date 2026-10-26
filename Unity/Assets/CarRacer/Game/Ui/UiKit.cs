using System;
using CarRacer.Core.Audio;
using CarRacer.Game.Audio;
using CarRacer.Game.Platform;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Ui
{
    /// <summary>
    /// The house style for UI Toolkit elements (design doc §3), built from code: a dark panel,
    /// big touch-sized buttons (64 reference pixels tall, about 9 mm on a phone) that darken while
    /// pressed, and a yellow accent. Font sizes follow the large-text option when the element is made,
    /// so a screen rebuilds its elements on <see cref="UiRoot.Restyle"/>.
    /// </summary>
    public static class UiKit
    {
        public const float ButtonHeight = 64f;

        public static readonly Color Background = new Color(0.10f, 0.12f, 0.18f);
        public static readonly Color PanelColour = new Color(0.06f, 0.07f, 0.11f, 0.88f);
        public static readonly Color ButtonColour = new Color(0.22f, 0.26f, 0.36f);
        public static readonly Color ButtonPressed = new Color(0.32f, 0.38f, 0.52f);
        public static readonly Color Accent = new Color(0.98f, 0.78f, 0.15f);
        public static readonly Color AccentPressed = new Color(1f, 0.88f, 0.45f);
        public static readonly Color FieldColour = new Color(0.16f, 0.18f, 0.25f);
        public static readonly Color Muted = new Color(0.70f, 0.74f, 0.82f);
        public static readonly Color Ink = new Color(0.08f, 0.08f, 0.1f);

        /// <summary>Text of <paramref name="size"/> reference pixels (before large text), in <paramref name="colour"/>.</summary>
        public static Label Text(string text, int size, Color colour, bool bold = false, bool wrap = false)
        {
            var label = new Label(text);
            label.style.fontSize = Accessibility.Text(size);
            label.style.color = colour;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            label.style.marginTop = label.style.marginBottom = 2f;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        public static Label Title(string text) => Centred(Text(text, 64, Accent, bold: true));

        public static Label Heading(string text) => Text(text, 34, Color.white, bold: true);

        public static Label Body(string text) => Text(text, 26, Color.white, wrap: true);

        public static Label Hint(string text) => Text(text, 21, Muted, wrap: true);

        public static Label Centred(Label label)
        {
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        /// <summary>
        /// A touch-sized button; <paramref name="primary"/> ones are yellow, for the main action on a
        /// page. Pressing it plays <paramref name="sound"/>: by default a confirm for primary buttons
        /// and a tap for the rest.
        /// </summary>
        public static Button Button(string text, Action onClick, bool primary = false, UiSound? sound = null)
        {
            // Primary buttons start something, so they confirm; the rest tick (design doc §13).
            UiSound tick = sound ?? (primary ? UiSound.Confirm : UiSound.Tap);
            var button = new Button(() =>
            {
                GameAudio.Play(tick);
                onClick?.Invoke();
            });
            button.text = text;
            Color normal = primary ? Accent : ButtonColour;
            Color pressed = primary ? AccentPressed : ButtonPressed;
            button.style.height = ButtonHeight;
            button.style.fontSize = Accessibility.Text(28);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = primary ? Ink : Color.white;
            button.style.backgroundColor = normal;
            button.style.marginTop = button.style.marginBottom = 8f;
            button.style.paddingLeft = button.style.paddingRight = 16f;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.overflow = Overflow.Hidden;
            Round(button, 10f);
            Border(button, 0f, Color.clear);

            // Feedback the moment a finger lands, before the click (which comes on release).
            button.RegisterCallback<PointerDownEvent>(_ => button.style.backgroundColor = pressed, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(_ => button.style.backgroundColor = normal, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = normal);
            return button;
        }

        /// <summary>
        /// A labelled rating bar: <paramref name="value"/> lit segments out of <paramref name="max"/>,
        /// with the number after them so the rating doesn't rest on colour alone (design doc §6).
        /// </summary>
        public static VisualElement Rating(string label, int value, int max, float labelWidth = 170f, float height = 22f, int textSize = 20)
        {
            var row = Row();
            row.style.alignItems = Align.Center;
            row.style.marginTop = row.style.marginBottom = 2f;
            row.style.height = height;
            // The label's room grows with large text, as its words do.
            var name = Text(label, textSize, Muted);
            name.style.width = labelWidth * Accessibility.Text(100) / 100f;
            name.style.flexShrink = 0f;
            row.Add(name);
            for (int i = 0; i < max; i++)
            {
                var segment = new VisualElement();
                segment.style.height = height * 0.6f;
                segment.style.flexGrow = 1f;
                segment.style.flexBasis = 0f;
                segment.style.marginRight = 4f;
                segment.style.backgroundColor = i < value ? Accent : FieldColour;
                segment.pickingMode = PickingMode.Ignore;
                Round(segment, 3f);
                row.Add(segment);
            }

            var number = Text(value.ToString(), textSize, Color.white);
            number.style.width = textSize * 1.2f;
            number.style.flexShrink = 0f;
            number.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(number);
            return row;
        }

        /// <summary>A panel of <paramref name="width"/> reference pixels (or as wide as it is given if 0).</summary>
        public static VisualElement Panel(float width = 0f)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = PanelColour;
            panel.style.paddingLeft = panel.style.paddingRight = 24f;
            panel.style.paddingTop = panel.style.paddingBottom = 20f;
            if (width > 0f)
            {
                panel.style.width = width;
                panel.style.maxWidth = Length.Percent(100);
            }

            Round(panel, 12f);
            return panel;
        }

        /// <summary>Children side by side; <paramref name="gap"/> reference pixels between them via <see cref="Space"/>.</summary>
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            return row;
        }

        /// <summary>A row of equal-width children with a gap between them.</summary>
        public static VisualElement EvenRow(params VisualElement[] children)
        {
            var row = Row();
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0)
                {
                    row.Add(Space(12f));
                }

                children[i].style.flexGrow = 1f;
                children[i].style.flexBasis = 0f;
                row.Add(children[i]);
            }

            return row;
        }

        public static VisualElement Space(float size)
        {
            var space = new VisualElement();
            space.style.width = size;
            space.style.height = size;
            space.style.flexShrink = 0f;
            space.pickingMode = PickingMode.Ignore;
            return space;
        }

        /// <summary>Takes up the room left in a row or column.</summary>
        public static VisualElement Grow()
        {
            var grow = new VisualElement();
            grow.style.flexGrow = 1f;
            grow.pickingMode = PickingMode.Ignore;
            return grow;
        }

        /// <summary>A one-line text box in the house style.</summary>
        public static TextField Field(string value, int maxLength, Action<string> onChange)
        {
            var field = new TextField { value = value, maxLength = maxLength };
            field.style.height = ButtonHeight;
            field.style.marginTop = field.style.marginBottom = 8f;
            var input = field.Q(className: TextField.inputUssClassName) ?? field;
            input.style.backgroundColor = FieldColour;
            input.style.color = Color.white;
            input.style.fontSize = Accessibility.Text(28);
            input.style.paddingLeft = input.style.paddingRight = 16f;
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            input.style.flexGrow = 1f;
            Round(input, 8f);
            field.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            return field;
        }

        /// <summary>Enables or disables a control, fading it while disabled (there is no theme to do it).</summary>
        public static T Enabled<T>(T element, bool enabled) where T : VisualElement
        {
            element.SetEnabled(enabled);
            element.style.opacity = enabled ? 1f : 0.45f;
            return element;
        }

        /// <summary>
        /// A square of <paramref name="colour"/> to tap; the <paramref name="chosen"/> one is drawn
        /// full size with a white edge, and an <paramref name="unavailable"/> one small and untappable.
        /// </summary>
        public static VisualElement Swatch(Color colour, bool chosen, bool unavailable, Action onPick, float size = ButtonHeight)
        {
            var cell = new VisualElement();
            cell.style.height = size;
            cell.style.flexGrow = 1f;
            cell.style.flexBasis = 0f;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.Center;
            var fill = new VisualElement();
            float inset = chosen ? 3f : unavailable ? size * 0.3f : 6f;
            fill.style.width = fill.style.height = size - 2f * inset;
            fill.style.backgroundColor = colour;
            fill.pickingMode = PickingMode.Ignore;
            Round(fill, 6f);
            if (chosen)
            {
                Border(fill, 3f, Color.white);
            }

            cell.Add(fill);
            if (unavailable || chosen)
            {
                cell.pickingMode = PickingMode.Ignore;
            }
            else
            {
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    GameAudio.Play(UiSound.Tap);
                    onPick?.Invoke();
                });
            }

            return cell;
        }

        /// <summary>Makes <paramref name="element"/> fill its parent, ignoring the layout flow.</summary>
        public static T Fill<T>(T element) where T : VisualElement
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0f;
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        public static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement element, float width, Color colour)
        {
            element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopWidth = element.style.borderBottomWidth = width;
            element.style.borderLeftColor = element.style.borderRightColor = colour;
            element.style.borderTopColor = element.style.borderBottomColor = colour;
        }
    }
}
