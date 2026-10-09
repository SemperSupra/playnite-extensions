using System;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaAssociationProposalFingerprintTests
    {
        private const string RootA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string RootB = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        private static MediaAssociationReviewSuggestion Proposal(params string[] files)
        {
            return new MediaAssociationReviewSuggestion
            {
                PlayniteId = "025b2cee-c509-4df6-a460-e5c72caf1e70",
                FileNames = files,
                MediaKindHint = "book",
                Rule = "EXACT_TITLE_STEM_REVIEW_ONLY",
                Disposition = "REVIEW_REQUIRED"
            };
        }

        [Fact]
        public void CanonicalDigestIgnoresVariantOrderButNotContent()
        {
            var a = MediaAssociationProposalFingerprint.Compute(
                Proposal("Synthetic.epub", "Synthetic.pdf"), RootA);
            var b = MediaAssociationProposalFingerprint.Compute(
                Proposal("Synthetic.pdf", "Synthetic.epub"), RootA);
            Assert.Equal(a, b);
            Assert.Equal(64, a.Length);
            Assert.True(MediaAssociationProposalFingerprint.MatchesCurrentProposal(
                a, Proposal("Synthetic.epub", "Synthetic.pdf"), RootA));
            Assert.False(MediaAssociationProposalFingerprint.MatchesCurrentProposal(
                a, Proposal("Synthetic.epub", "Changed.pdf"), RootA));
        }

        [Fact]
        public void ChangedRootOrIdentityInvalidatesPriorProposal()
        {
            var old = Proposal("Synthetic.epub");
            var digest = MediaAssociationProposalFingerprint.Compute(old, RootA);
            Assert.False(MediaAssociationProposalFingerprint.MatchesCurrentProposal(
                digest, old, RootB));
            var anotherGame = Proposal("Synthetic.epub");
            anotherGame.PlayniteId = "a97642ae-1118-4f9b-af76-9ab5d5330d54";
            Assert.False(MediaAssociationProposalFingerprint.MatchesCurrentProposal(
                digest, anotherGame, RootA));
        }

        [Fact]
        public void MissingOrMalformedRootBindingFailsClosed()
        {
            Assert.Throws<ArgumentException>(() =>
                MediaAssociationProposalFingerprint.Compute(Proposal("Book.pdf"), ""));
            Assert.Throws<ArgumentException>(() =>
                MediaAssociationProposalFingerprint.Compute(Proposal("Book.pdf"),
                    new string('z', 64)));
            Assert.False(MediaAssociationProposalFingerprint.MatchesCurrentProposal(
                "no-evidence", Proposal("Book.pdf"), RootA));
        }

        [Fact]
        public void NoApprovalOrApplyAuthorityCanBeImplied()
        {
            var candidate = Proposal("Book.epub");
            candidate.Disposition = "APPROVED_FOR_APPLY";
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(candidate, RootA));
            candidate.Disposition = "REVIEW_REQUIRED";
            candidate.Rule = "FUZZY_TITLE_ACCEPTANCE";
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(candidate, RootA));
        }

        [Theory]
        [InlineData("../Book.epub")]
        [InlineData(@"C:\escape\Book.pdf")]
        [InlineData("Book:copy.pdf")]
        [InlineData("Book.pdf ")]
        [InlineData("Book.txt")]
        [InlineData("Book\u0001.epub")]
        [InlineData("Book\u202E.epub")]
        [InlineData("Book\u200D.pdf")]
        public void UntrustedFilenamesFailClosed(string name)
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(Proposal(name), RootA));
        }

        [Fact]
        public void DuplicateNameOrChangedKindFailsClosed()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(
                    Proposal("Book.pdf", "book.PDF"), RootA));
            var inconsistent = Proposal("Book.pdf");
            inconsistent.MediaKindHint = "audio";
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(inconsistent, RootA));
        }

        [Fact]
        public void EmptyOrOverlargeProposalFailsClosed()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(Proposal(), RootA));
            var many = new string[65];
            for (int i = 0; i < many.Length; i++)
            {
                many[i] = "Synthetic-" + i + ".pdf";
            }
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationProposalFingerprint.Compute(Proposal(many), RootA));
        }
    }
}
