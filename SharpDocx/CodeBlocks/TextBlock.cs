using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using SharpDocx.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace SharpDocx.CodeBlocks
{
    /// <summary>
    /// A TextBlock is a CodeBlock with an opening curly brace and @, textual content and a CodeBlock with a closing curly brace. E.g.:
    /// <code>
    /// &lt;% for (int i = 0; i &lt; 10; ++i) {@ %&gt;
    ///   Textual content.
    /// &lt;% } %&gt;
    /// </code>
    /// </summary>
    public class TextBlock : CodeBlock
    {
        internal CodeBlock EndingCodeBlock;
        internal Paragraph FirstInsertionPointParagraph;

        private Appender _appender;
        private readonly Body _body = new Body();

        public TextBlock(string code) : base(code)
        {
            CurrentInsertionPoint = new InsertionPoint();
        }

        internal override void Initialize()
        {
            base.Initialize();

            FirstInsertionPointParagraph =
                Placeholder.GetElementBlockLevelParent().InsertBeforeSelf(new Paragraph());
            FirstInsertionPointParagraph.SetAttribute(new OpenXmlAttribute(null, "IpId", null, CurrentInsertionPoint.Id));
            CurrentInsertionPoint.Element = FirstInsertionPointParagraph;

            GetBody(StartText, EndingCodeBlock.Placeholder);
            _appender = new Appender(_body);
        }

        public void Append(List<CodeBlock> codeBlocks)
        {
            var previousInsertionPoint = CurrentInsertionPoint.Element;

            CurrentInsertionPoint.Element = _appender.Append(CurrentInsertionPoint.Element);

            RemoveEmptyWrapperParagraphs(previousInsertionPoint);

            // Update insertion points of *other* code blocks within this text block (e.g. the insert point of a row appender).
            var childInsertionPoints = _appender.GetInsertionPoints();

            foreach (var childInsertionPoint in childInsertionPoints)
            {
                codeBlocks.First(x => x.CurrentInsertionPoint?.Id == childInsertionPoint.Id)
                    .CurrentInsertionPoint.Element = childInsertionPoint.Element;
            }
        }

        private void RemoveEmptyWrapperParagraphs(OpenXmlCompositeElement previousInsertionPoint)
        {
            var first = previousInsertionPoint.NextSibling() as Paragraph;
            var last = CurrentInsertionPoint.Element as Paragraph;

            if (first == null || last == null) return;

            if (first == last)
            {
                if (!first.HasText() && !ParagraphHasSectionProperties(first))
                {
                    first.Remove();
                    CurrentInsertionPoint.Element = previousInsertionPoint;
                }
                return;
            }

            // ── Opening wrapper paragraph ─────────────────────────────────────────
            if (!ParagraphHasSectionProperties(first))
            {
                if (!first.HasText())
                {
                    first.Remove();
                }
                else
                {
                    // Prefix text before the opening tag: merge into the first content paragraph,
                    // but only when there is at least one content paragraph between first and last.
                    var firstNext = first.NextSibling() as Paragraph;
                    if (firstNext != null && firstNext != last)
                    {
                        MergeIntoNextParagraph(first);
                    }
                }
            }

            if (last.Parent == null || ParagraphHasSectionProperties(last)) return;

            // ── Closing wrapper paragraph ─────────────────────────────────────────
            if (!last.HasText())
            {
                var newLast = last.PreviousSibling() as OpenXmlCompositeElement;
                last.Remove();
                if (newLast != null)
                    CurrentInsertionPoint.Element = newLast;
            }
            else
            {
                // Only merge when the text comes exclusively from the suffix after %>,
                // i.e. nothing appeared BEFORE the closing <% } %> tag in the template.
                bool hasSuffix = (EndingCodeBlock.EndText?.Text?.Length ?? 0) > 0;
                bool hasContentBefore = (EndingCodeBlock.StartText?.Text?.Length ?? 0) > 0;

                if (hasSuffix && !hasContentBefore)
                {
                    var lastPrev = last.PreviousSibling() as Paragraph;
                    if (lastPrev != null)
                    {
                        var newLast = lastPrev as OpenXmlCompositeElement;
                        MergeIntoPreviousParagraph(last);
                        CurrentInsertionPoint.Element = newLast;
                    }
                }
                // else: content before closing tag → keep paragraph as-is
            }
        }

        // Moves all non-ParagraphProperties children of source to the beginning
        // of the next sibling paragraph, then removes source.
        private static void MergeIntoNextParagraph(Paragraph source)
        {
            var next = source.NextSibling() as Paragraph;
            if (next == null) return;

            var toMove = source.ChildElements
                .Where(c => !(c is ParagraphProperties))
                .ToList();

            // Insert in order, right after the target's ParagraphProperties (if any).
            OpenXmlElement cursor = next.ParagraphProperties;
            foreach (var element in toMove)
            {
                element.Remove();
                if (cursor != null)
                {
                    cursor.InsertAfterSelf(element);
                    cursor = element;
                }
                else if (next.FirstChild != null)
                {
                    next.InsertBefore(element, next.FirstChild);
                    cursor = element;
                }
                else
                {
                    next.AppendChild(element);
                    cursor = element;
                }
            }

            source.Remove();
        }

        // Appends all non-ParagraphProperties children of source to the end
        // of the previous sibling paragraph, then removes source.
        private static void MergeIntoPreviousParagraph(Paragraph source)
        {
            var prev = source.PreviousSibling() as Paragraph;
            if (prev == null) return;

            var toMove = source.ChildElements
                .Where(c => !(c is ParagraphProperties))
                .ToList();

            foreach (var element in toMove)
            {
                element.Remove();
                prev.AppendChild(element);
            }

            source.Remove();
        }

        private static bool ParagraphHasSectionProperties(Paragraph paragraph)
        {
            return paragraph.Descendants<SectionProperties>().Any();
        }

        private static SectionProperties GetSectionProperties(OpenXmlElement element)
        {
            if (element is Paragraph paragraph)
            {
                return paragraph.ParagraphProperties?.SectionProperties;
            }

            return null;
        }

        private static SectionProperties FindNextRemainingSectionProperties(OpenXmlElement element, HashSet<OpenXmlElement> removedElements)
        {
            var next = element.NextSibling();
            while (next != null)
            {
                if (!removedElements.Contains(next))
                {
                    var nextSectionProperties = GetSectionProperties(next);
                    if (nextSectionProperties != null)
                    {
                        return nextSectionProperties;
                    }
                }

                next = next.NextSibling();
            }

            if (element.Parent is Body body)
            {
                return body.ChildElements.OfType<SectionProperties>().LastOrDefault();
            }

            return null;
        }

        private static void PropagateHeaderFooterReferences(SectionProperties sourceSectionProperties, SectionProperties targetSectionProperties)
        {
            foreach (var headerReference in sourceSectionProperties.Elements<HeaderReference>())
            {
                var headerType = headerReference.Type?.Value;
                var alreadyExists = targetSectionProperties
                    .Elements<HeaderReference>()
                    .Any(x => x.Type?.Value == headerType);

                if (!alreadyExists)
                {
                    targetSectionProperties.Append((HeaderReference)headerReference.CloneNode(true));
                }
            }

            foreach (var footerReference in sourceSectionProperties.Elements<FooterReference>())
            {
                var footerType = footerReference.Type?.Value;
                var alreadyExists = targetSectionProperties
                    .Elements<FooterReference>()
                    .Any(x => x.Type?.Value == footerType);

                if (!alreadyExists)
                {
                    targetSectionProperties.Append((FooterReference)footerReference.CloneNode(true));
                }
            }
        }

        private static void PreserveSectionHeaderFooterReferences(IEnumerable<OpenXmlElement> elementsToRemove)
        {
            var list = elementsToRemove.ToList();
            var removedElements = new HashSet<OpenXmlElement>(list);

            foreach (var element in list)
            {
                var sourceSectionProperties = GetSectionProperties(element);
                if (sourceSectionProperties == null)
                {
                    continue;
                }

                var targetSectionProperties = FindNextRemainingSectionProperties(element, removedElements);
                if (targetSectionProperties == null || ReferenceEquals(sourceSectionProperties, targetSectionProperties))
                {
                    continue;
                }

                PropagateHeaderFooterReferences(sourceSectionProperties, targetSectionProperties);
            }
        }

        internal void GetBody(OpenXmlElement startText, OpenXmlElement endText)
        {
            var startParent = startText.GetElementBlockLevelParent();
            var endParent = endText.GetElementBlockLevelParent();

            if (startParent == endParent)
            {
                PreserveSectionHeaderFooterReferences(new[] { startParent });
                startParent.Remove();
                _body.InsertAt(startParent, 0);
                return;
            }

            var elementsToMove = new List<OpenXmlElement>();
            var nextElement = (OpenXmlElement)startParent;
            while (nextElement != endParent)
            {
                elementsToMove.Add(nextElement);
                nextElement = nextElement.NextSibling();
            }
            elementsToMove.Add(endParent);

            PreserveSectionHeaderFooterReferences(elementsToMove);

            foreach (var element in elementsToMove)
            {
                element.Remove();
                _body.InsertAt(element, _body.ChildElements.Count);
            }
        }
    }
}