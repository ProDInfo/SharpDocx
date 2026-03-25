using System.Linq;
using DocumentFormat.OpenXml.Wordprocessing;
using SharpDocx.Extensions;

namespace SharpDocx.CodeBlocks
{
    public class CodeBlock
    {
        public string Code { get; }

        public Text Placeholder { get; internal set; }

        internal Text StartText { get; set; }

        internal Text EndText { get; set; }

        internal InsertionPoint CurrentInsertionPoint { get; set; }

        private CodeBlock()
        {
            CurrentInsertionPoint = null;
        }

        internal CodeBlock(string code)
        {
            Code = code;
        }

        internal void RemoveEmptyParagraphs()
        {
            var startParagraph = StartText?.GetParent<Paragraph>();
            if (startParagraph?.Parent != null && CanDeleteParagraph(startParagraph))
            {
                // For non-TextBlock flow-control blocks (e.g. {! foreach, closing }),
                // use template-level info: if there is no text before <% and no text after %>
                // in the original template, the paragraph is a pure wrapper and should
                // always be removed, regardless of what HasText() reports on the clone.
                bool noSurroundingText = (StartText?.Text?.Length ?? 0) == 0
                    && (EndText?.Text?.Length ?? 0) == 0;
                bool isFlowControl = !(this is TextBlock) && IsFlowControlCode(Code);

                if (!startParagraph.HasText() || (isFlowControl && noSurroundingText))
                {
                    startParagraph.Remove();
                }
            }

            var endParagraph = EndText?.GetParent<Paragraph>();
            if (endParagraph?.Parent != null &&
                !endParagraph.HasText() &&
                CanDeleteParagraph(endParagraph))
            {
                endParagraph.Remove();
            }

            var tb = this as TextBlock;
            if (tb?.FirstInsertionPointParagraph.Parent != null) 
            {
                tb.FirstInsertionPointParagraph.Remove();
            }
        }

        // Returns true for code that opens or closes a flow-control block
        // and therefore produces a wrapper-only paragraph with no visible content.
        private static bool IsFlowControlCode(string code)
        {
            if (code == null) return false;
            var trimmed = code.Trim();
            return trimmed.EndsWith("{")
                || trimmed == "}"
                || trimmed.StartsWith("}");
        }

        private bool CanDeleteParagraph(Paragraph paragraph)
        {
            if (paragraph.Descendants<SectionProperties>().Any())
            {
                // Keep section break paragraphs to preserve header/footer references.
                return false;
            }

            if (paragraph.Parent is TableCell)
            {
                // TableCell should have at least one paragraph element.
                var count = paragraph.Parent.ChildElements.Count(c => c is Paragraph);
                return count > 1;
            }

            return true;
        }

        internal virtual void Initialize()
        {
        }
    }
}