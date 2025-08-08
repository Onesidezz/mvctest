using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;
using System.Text.RegularExpressions;

namespace mvctest.Services
{
    public class TextEmbeddingService : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly int _maxTokens = 512;
        private readonly int _chunkSize = 300; // words per chunk
        private readonly int _chunkOverlap = 50; // overlapping words

        public TextEmbeddingService(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX model not found at: {modelPath}");

            _session = new InferenceSession(modelPath);
        }

        public float[] GetEmbedding(string text)
        {
            try
            {
                // Simple tokenization (for demonstration - use proper tokenizer for production)
                var tokens = SimpleTokenize(text);
                
                // Create input tensors
                var inputIds = new DenseTensor<long>(new[] { 1, tokens.Length });
                var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });

                for (int i = 0; i < tokens.Length; i++)
                {
                    inputIds[0, i] = tokens[i];
                    attentionMask[0, i] = 1;
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
                };

                using var results = _session.Run(inputs);
                
                // Handle different output formats
                var output = results.FirstOrDefault();
                if (output == null)
                {
                    Console.WriteLine("No output from ONNX model");
                    return new float[768];
                }

                var embeddings = output.AsTensor<float>();
                
                // Handle different embedding output shapes
                if (embeddings.Dimensions.Length == 2)
                {
                    // Shape: [batch_size, embedding_size] - already pooled
                    var embeddingSize = embeddings.Dimensions[1];
                    var sentenceEmbedding = new float[embeddingSize];
                    for (int i = 0; i < embeddingSize; i++)
                    {
                        sentenceEmbedding[i] = embeddings[0, i];
                    }
                    return sentenceEmbedding;
                }
                else if (embeddings.Dimensions.Length == 3)
                {
                    // Shape: [batch_size, sequence_length, embedding_size] - need pooling
                    var embeddingSize = embeddings.Dimensions[2];
                    var sequenceLength = embeddings.Dimensions[1];
                    var sentenceEmbedding = new float[embeddingSize];
                    
                    // Mean pooling
                    for (int i = 0; i < embeddingSize; i++)
                    {
                        float sum = 0;
                        for (int j = 0; j < sequenceLength; j++)
                        {
                            sum += embeddings[0, j, i];
                        }
                        sentenceEmbedding[i] = sum / sequenceLength;
                    }
                    return sentenceEmbedding;
                }
                else
                {
                    Console.WriteLine($"Unexpected embedding shape: {string.Join(",", embeddings.Dimensions.ToArray())}");
                    return new float[768];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating embedding: {ex.Message}");
                // Return zero vector as fallback
                return new float[768]; // Common embedding size
            }
        }

        private long[] SimpleTokenize(string text)
        {
            // Simple word-based tokenization with proper vocabulary bounds
            var cleanText = Regex.Replace(text.ToLower(), @"[^\w\s]", " ");
            var words = cleanText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            var tokens = new List<long> { 101 }; // [CLS] token
            
            // Use vocabulary size based on the error message: [-30522, 30521] = 61043 tokens
            const int vocabSize = 30521; // Max positive token ID
            const int minTokenId = 999;  // Start from 999 to avoid special tokens
            
            foreach (var word in words.Take(_maxTokens - 2))
            {
                // Generate token ID within valid range
                var hash = Math.Abs(word.GetHashCode());
                var tokenId = (hash % (vocabSize - minTokenId)) + minTokenId;
                
                // Ensure it's within bounds
                tokenId = Math.Max(minTokenId, Math.Min(vocabSize, tokenId));
                tokens.Add(tokenId);
            }
            
            tokens.Add(102); // [SEP] token
            
            // Pad to fixed length with [PAD] token (0)
            while (tokens.Count < _maxTokens)
                tokens.Add(0);
                
            return tokens.Take(_maxTokens).ToArray();
        }

        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return 0f;

            float dotProduct = 0f;
            float normA = 0f;
            float normB = 0f;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }

    public class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = new float[0];
        public string Source { get; set; } = string.Empty;
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}