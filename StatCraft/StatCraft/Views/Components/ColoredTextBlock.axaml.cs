using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using StatCraft.Styles;

namespace StatCraft.Views.Components
{
    public partial class ColoredTextBlock : UserControl
    {
        // Picks a run's own colour, or falls back to this control's inherited Foreground when it has
        // none. Binding a TextBlock's Foreground to null would paint nothing, and letting the binding
        // yield UnsetValue resets it to TextBlock's own default (black) rather than the inherited value —
        // both verified — so the fallback has to be supplied explicitly.
        public static readonly IMultiValueConverter ColorOrInherited =
            new FuncMultiValueConverter<object?, IBrush?>(values => values.OfType<IBrush>().FirstOrDefault());

        public static readonly StyledProperty<IEnumerable<ColoredCharacter>?> CharactersProperty =
            AvaloniaProperty.Register<ColoredTextBlock, IEnumerable<ColoredCharacter>?>(nameof(Characters));

        public IEnumerable<ColoredCharacter>? Characters
        {
            get => GetValue(CharactersProperty);
            set => SetValue(CharactersProperty, value);
        }

        public ColoredTextBlock()
        {
            InitializeComponent();
        }
    }
}
